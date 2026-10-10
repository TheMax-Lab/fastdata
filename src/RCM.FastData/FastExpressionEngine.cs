using System;
using System.Collections.Generic;
using System.Globalization;

namespace RCM.FastData
{
    internal sealed class FastExpressionEngine
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly FastDataTable _table;
        private bool _dirty = true;
        private bool _hasExpressions;

        private readonly Dictionary<int, CompiledExpression> _compiled = new Dictionary<int, CompiledExpression>();
        private int[] _evalOrder = new int[0];
        private readonly Dictionary<int, int[]> _affectedByInput = new Dictionary<int, int[]>();
        private readonly Dictionary<string, RelationWatch> _watchByRelation =
            new Dictionary<string, RelationWatch>(StringComparer.OrdinalIgnoreCase);

        internal FastExpressionEngine(FastDataTable table)
        {
            _table = table;
        }

        internal void MarkDirty()
        {
            _dirty = true;
        }

        internal bool TryGetRelationWatch(string relationName, out RelationWatch watch)
        {
            EnsureCompiled();
            return _watchByRelation.TryGetValue(relationName, out watch);
        }

        internal void RecalculateAllRows()
        {
            EnsureCompiled();
            if (!_hasExpressions) return;

            List<FastDataRow> rows = _table.Rows.InternalList;
            for (int i = 0; i < rows.Count; i++)
            {
                FastDataRow row = rows[i];
                if (row.RowState == DataRowState.Deleted) continue;
                RecalculateRowInternal(row, _evalOrder);
            }
        }

        internal void RecalculateRow(FastDataRow row, int changedInputOrdinal)
        {
            if (row == null || row.RowState == DataRowState.Deleted) return;

            EnsureCompiled();
            if (!_hasExpressions) return;

            int[] ords;
            if (changedInputOrdinal >= 0 && _affectedByInput.TryGetValue(changedInputOrdinal, out ords))
                RecalculateRowInternal(row, ords);
            else
                RecalculateRowInternal(row, _evalOrder);
        }

        private void RecalculateRowInternal(FastDataRow row, int[] exprOrdinals)
        {
            if (exprOrdinals == null || exprOrdinals.Length == 0) return;

            _table.SetExpressionRecalcFlag(true);
            try
            {
                for (int i = 0; i < exprOrdinals.Length; i++)
                {
                    int ord = exprOrdinals[i];
                    CompiledExpression ce = _compiled[ord];

                    object raw = ce.Root.Eval(row, _table);
                    object coerced = CoerceForColumn(raw, _table.Columns[ord].DataType);

                    row.SetCalculatedValue(ord, coerced);
                }
            }
            finally
            {
                _table.SetExpressionRecalcFlag(false);
            }
        }

        private void EnsureCompiled()
        {
            if (!_dirty) return;
            Compile();
            _dirty = false;
        }

        private void Compile()
        {
            _compiled.Clear();
            _affectedByInput.Clear();
            _watchByRelation.Clear();
            _evalOrder = new int[0];
            _hasExpressions = false;

            List<int> exprOrdinals = new List<int>();
            List<int> inputOrdinals = new List<int>();

            for (int i = 0; i < _table.Columns.Count; i++)
            {
                if (_table.Columns[i].HasExpression) exprOrdinals.Add(i);
                else inputOrdinals.Add(i);
            }

            if (exprOrdinals.Count == 0) return;
            _hasExpressions = true;

            // Parse  
            for (int i = 0; i < exprOrdinals.Count; i++)
            {
                int ord = exprOrdinals[i];
                FastDataColumn col = _table.Columns[ord];

                Parser p = new Parser(col.Expression, _table);
                ExprNode root = p.ParseExpression();
                p.EnsureEnd();

                if (ContainsLooseChildRef(root))
                {
                    throw new InvalidOperationException(
                        "Expression non valida per la colonna '" + col.ColumnName +
                        "': Child(...) è consentito solo dentro SUM/AVG/MIN/MAX/COUNT.");
                }

                CompiledExpression ce = new CompiledExpression
                {
                    Ordinal = ord,
                    Root = root,
                    DirectColumnDependencies = ToArray(p.ColumnDependencies),
                    ChildAggregates = p.ChildAggregates.ToArray()
                };

                _compiled.Add(ord, ce);
            }

            // Build dep graph: expr -> expr  
            Dictionary<int, List<int>> reverse = new Dictionary<int, List<int>>(exprOrdinals.Count);
            Dictionary<int, int> indegree = new Dictionary<int, int>(exprOrdinals.Count);

            for (int i = 0; i < exprOrdinals.Count; i++)
            {
                int e = exprOrdinals[i];
                reverse[e] = new List<int>(2);
                indegree[e] = 0;
            }

            for (int i = 0; i < exprOrdinals.Count; i++)
            {
                CompiledExpression ce = _compiled[exprOrdinals[i]];

                HashSet<int> exprDeps = new HashSet<int>();
                for (int d = 0; d < ce.DirectColumnDependencies.Length; d++)
                {
                    int dep = ce.DirectColumnDependencies[d];
                    if (_table.Columns[dep].HasExpression)
                        exprDeps.Add(dep);
                }

                bool unresolvedChild = false;
                for (int a = 0; a < ce.ChildAggregates.Length; a++)
                {
                    ChildAggregateRef agg = ce.ChildAggregates[a];
                    FastDataRelation rel = _table.DataSet != null ? _table.DataSet.Relations[agg.RelationName] : null;

                    if (rel != null && ReferenceEquals(rel.ParentTable, _table))
                        agg.ParentKeyOrdinal = rel.ParentColumn.Ordinal;
                    else
                        unresolvedChild = true;
                }

                ce.DependsOnAllInputColumns = unresolvedChild;
                ce.ExpressionDependencies = ToArray(exprDeps);

                indegree[ce.Ordinal] = ce.ExpressionDependencies.Length;
                for (int x = 0; x < ce.ExpressionDependencies.Length; x++)
                {
                    int depExpr = ce.ExpressionDependencies[x];
                    reverse[depExpr].Add(ce.Ordinal);
                }
            }

            // Topo sort  
            Queue<int> q = new Queue<int>(exprOrdinals.Count);
            for (int i = 0; i < exprOrdinals.Count; i++)
            {
                int e = exprOrdinals[i];
                if (indegree[e] == 0) q.Enqueue(e);
            }

            List<int> order = new List<int>(exprOrdinals.Count);
            while (q.Count > 0)
            {
                int e = q.Dequeue();
                order.Add(e);

                List<int> dep = reverse[e];
                for (int i = 0; i < dep.Count; i++)
                {
                    int nxt = dep[i];
                    indegree[nxt] = indegree[nxt] - 1;
                    if (indegree[nxt] == 0) q.Enqueue(nxt);
                }
            }

            if (order.Count != exprOrdinals.Count)
                throw new InvalidOperationException("Dipendenza ciclica tra expression columns.");

            _evalOrder = order.ToArray();

            Dictionary<int, int> orderPos = new Dictionary<int, int>(_evalOrder.Length);
            for (int i = 0; i < _evalOrder.Length; i++) orderPos[_evalOrder[i]] = i;

            // Build input->affected expressions map  
            Dictionary<int, List<int>> startByInput = new Dictionary<int, List<int>>();
            for (int i = 0; i < inputOrdinals.Count; i++)
                startByInput[inputOrdinals[i]] = new List<int>(2);

            for (int i = 0; i < exprOrdinals.Count; i++)
            {
                CompiledExpression ce = _compiled[exprOrdinals[i]];

                if (ce.DependsOnAllInputColumns)
                {
                    for (int j = 0; j < inputOrdinals.Count; j++)
                        AddUnique(startByInput[inputOrdinals[j]], ce.Ordinal);
                }
                else
                {
                    for (int d = 0; d < ce.DirectColumnDependencies.Length; d++)
                    {
                        int dep = ce.DirectColumnDependencies[d];
                        if (!_table.Columns[dep].HasExpression)
                            AddUnique(startByInput[dep], ce.Ordinal);
                    }

                    for (int a = 0; a < ce.ChildAggregates.Length; a++)
                    {
                        int pk = ce.ChildAggregates[a].ParentKeyOrdinal;
                        if (pk >= 0 && !_table.Columns[pk].HasExpression)
                            AddUnique(startByInput[pk], ce.Ordinal);
                    }
                }
            }

            foreach (KeyValuePair<int, List<int>> kv in startByInput)
            {
                if (kv.Value.Count == 0) continue;

                HashSet<int> closure = new HashSet<int>(kv.Value);
                Queue<int> walk = new Queue<int>(kv.Value);

                while (walk.Count > 0)
                {
                    int e = walk.Dequeue();
                    List<int> dep = reverse[e];

                    for (int i = 0; i < dep.Count; i++)
                    {
                        int nxt = dep[i];
                        if (closure.Add(nxt)) walk.Enqueue(nxt);
                    }
                }

                List<int> ordered = new List<int>(closure);
                ordered.Sort(delegate (int a, int b) { return orderPos[a].CompareTo(orderPos[b]); });

                _affectedByInput[kv.Key] = ordered.ToArray();
            }

            // Build relation watches  
            Dictionary<string, HashSet<int>> directByRel =
                new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, HashSet<string>> colsByRel =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < exprOrdinals.Count; i++)
            {
                CompiledExpression ce = _compiled[exprOrdinals[i]];
                for (int a = 0; a < ce.ChildAggregates.Length; a++)
                {
                    ChildAggregateRef agg = ce.ChildAggregates[a];

                    HashSet<int> exprSet;
                    if (!directByRel.TryGetValue(agg.RelationName, out exprSet))
                    {
                        exprSet = new HashSet<int>();
                        directByRel.Add(agg.RelationName, exprSet);
                    }
                    exprSet.Add(ce.Ordinal);

                    HashSet<string> colSet;
                    if (!colsByRel.TryGetValue(agg.RelationName, out colSet))
                    {
                        colSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        colsByRel.Add(agg.RelationName, colSet);
                    }
                    colSet.Add(agg.ChildColumnName);
                }
            }

            foreach (KeyValuePair<string, HashSet<int>> kv in directByRel)
            {
                string relName = kv.Key;

                HashSet<int> closure = new HashSet<int>(kv.Value);
                Queue<int> walk = new Queue<int>(kv.Value);

                while (walk.Count > 0)
                {
                    int e = walk.Dequeue();
                    List<int> dep = reverse[e];

                    for (int i = 0; i < dep.Count; i++)
                    {
                        int nxt = dep[i];
                        if (closure.Add(nxt)) walk.Enqueue(nxt);
                    }
                }

                List<int> ordered = new List<int>(closure);
                ordered.Sort(delegate (int a, int b) { return orderPos[a].CompareTo(orderPos[b]); });

                HashSet<int> childOrdinals = new HashSet<int>();
                bool matchAny = false;

                FastDataRelation rel = _table.DataSet != null ? _table.DataSet.Relations[relName] : null;
                if (rel == null)
                {
                    matchAny = true;
                }
                else
                {
                    foreach (string childCol in colsByRel[relName])
                    {
                        if (rel.ChildTable.Columns.Contains(childCol))
                            childOrdinals.Add(rel.ChildTable.Columns.GetOrdinal(childCol));
                        else
                            matchAny = true;
                    }
                }

                _watchByRelation[relName] = new RelationWatch(relName, ordered.ToArray(), childOrdinals, matchAny);
            }
        }

        private static int[] ToArray(HashSet<int> set)
        {
            int[] arr = new int[set.Count];
            set.CopyTo(arr);
            Array.Sort(arr);
            return arr;
        }

        private static void AddUnique(List<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return;
            list.Add(value);
        }

        private static bool ContainsLooseChildRef(ExprNode node)
        {
            if (node == null) return false;
            if (node is ChildColumnRefNode) return true;
            if (node is ConstantNode || node is ColumnNode || node is ChildAggregateNode) return false;

            UnaryNode un = node as UnaryNode;
            if (un != null) return ContainsLooseChildRef(un.Inner);

            BinaryNode bn = node as BinaryNode;
            if (bn != null) return ContainsLooseChildRef(bn.Left) || ContainsLooseChildRef(bn.Right);

            FunctionNode fn = node as FunctionNode;
            if (fn != null)
            {
                for (int i = 0; i < fn.Args.Length; i++)
                    if (ContainsLooseChildRef(fn.Args[i])) return true;
            }

            return false;
        }

        private static object CoerceForColumn(object value, Type targetType)
        {
            if (FastKey.IsNull(value)) return DBNull.Value;
            if (targetType == null || targetType == typeof(object)) return value;

            Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (t.IsInstanceOfType(value)) return value;

            if (t == typeof(string)) return Convert.ToString(value, Inv);
            if (t == typeof(bool)) return ToBoolean(value);

            return Convert.ChangeType(value, t, Inv);
        }

        private static bool ToBoolean(object value)
        {
            if (FastKey.IsNull(value)) return false;
            if (value is bool) return (bool)value;

            string s = value as string;
            if (s != null)
            {
                s = s.Trim();
                if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "0", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }

            decimal d;
            if (TryAsDecimal(value, out d)) return d != 0m;

            return Convert.ToBoolean(value, Inv);
        }

        private static bool TryAsDouble(object value, out double d)
        {
            if (FastKey.IsNull(value)) { d = 0d; return false; }
            // Treats empty strings as 0 for compatibility with expressions
            if (value is string s && s.Length == 0) { d = 0d; return true; }
            if (value is double) { d = (double)value; return true; }
            if (value is float) { d = (float)value; return true; }
            if (value is decimal) { d = (double)(decimal)value; return true; }
            if (value is int) { d = (int)value; return true; }
            if (value is long) { d = (long)value; return true; }
            if (value is short) { d = (short)value; return true; }
            if (value is byte) { d = (byte)value; return true; }
            return double.TryParse(Convert.ToString(value, Inv), NumberStyles.Any, Inv, out d);
        }

        private static bool TryAsDecimal(object value, out decimal d)
        {
            if (FastKey.IsNull(value)) { d = 0m; return false; }
            // Treats empty strings as 0 for compatibility with expressions
            if (value is string s && s.Length == 0) { d = 0m; return true; }
            if (value is decimal) { d = (decimal)value; return true; }
            if (value is int) { d = (int)value; return true; }
            if (value is long) { d = (long)value; return true; }
            if (value is short) { d = (short)value; return true; }
            if (value is byte) { d = (byte)value; return true; }

            if (value is double)
            {
                try { d = (decimal)(double)value; return true; } catch { d = 0m; return false; }
            }
            if (value is float)
            {
                try { d = (decimal)(float)value; return true; } catch { d = 0m; return false; }
            }

            try
            {
                d = Convert.ToDecimal(value, Inv);
                return true;
            }
            catch
            {
                d = 0m;
                return false;
            }
        }

        internal sealed class RelationWatch
        {
            internal readonly string RelationName;
            internal readonly int[] AffectedExpressionOrdinals;
            internal readonly HashSet<int> ChildValueOrdinals;
            internal readonly bool MatchAnyChildOrdinal;

            internal RelationWatch(string relationName, int[] affectedExprOrdinals, HashSet<int> childValueOrdinals, bool matchAnyChildOrdinal)
            {
                RelationName = relationName;
                AffectedExpressionOrdinals = affectedExprOrdinals ?? new int[0];
                ChildValueOrdinals = childValueOrdinals ?? new HashSet<int>();
                MatchAnyChildOrdinal = matchAnyChildOrdinal;
            }

            internal bool IsChildValueOrdinalRelevant(int ordinal)
            {
                return ChildValueOrdinals.Contains(ordinal);
            }
        }

        private sealed class CompiledExpression
        {
            internal int Ordinal;
            internal ExprNode Root;
            internal int[] DirectColumnDependencies = new int[0];
            internal int[] ExpressionDependencies = new int[0];
            internal ChildAggregateRef[] ChildAggregates = new ChildAggregateRef[0];
            internal bool DependsOnAllInputColumns;
        }

        private sealed class ChildAggregateRef
        {
            internal AggregateFunction Function;
            internal string RelationName;
            internal string ChildColumnName;
            internal int ParentKeyOrdinal = -1;
        }

        private enum AggregateFunction
        {
            Sum,
            Avg,
            Min,
            Max,
            Count
        }

        #region AST  

        private abstract class ExprNode
        {
            internal abstract object Eval(FastDataRow row, FastDataTable table);
        }

        private sealed class ConstantNode : ExprNode
        {
            private readonly object _value;
            internal ConstantNode(object value) { _value = value; }
            internal override object Eval(FastDataRow row, FastDataTable table) { return _value; }
        }

        private sealed class ColumnNode : ExprNode
        {
            private readonly int _ordinal;
            internal ColumnNode(int ordinal) { _ordinal = ordinal; }
            internal override object Eval(FastDataRow row, FastDataTable table) { return row.GetValue(_ordinal); }
        }

        private sealed class ChildColumnRefNode : ExprNode
        {
            internal readonly string RelationName;
            internal readonly string ColumnName;

            internal ChildColumnRefNode(string relationName, string columnName)
            {
                RelationName = relationName;
                ColumnName = columnName;
            }

            internal override object Eval(FastDataRow row, FastDataTable table)
            {
                throw new InvalidOperationException("Child(...) ammesso solo dentro SUM/AVG/MIN/MAX/COUNT.");
            }
        }

        private enum UnaryOperator
        {
            Plus,
            Minus,
            Not
        }

        private sealed class UnaryNode : ExprNode
        {
            internal readonly UnaryOperator Op;
            internal readonly ExprNode Inner;

            internal UnaryNode(UnaryOperator op, ExprNode inner)
            {
                Op = op;
                Inner = inner;
            }

            internal override object Eval(FastDataRow row, FastDataTable table)
            {
                object v = Inner.Eval(row, table);

                switch (Op)
                {
                    case UnaryOperator.Plus:
                        return FastKey.IsNull(v) ? (object)DBNull.Value : v;

                    case UnaryOperator.Minus:
                        if (FastKey.IsNull(v)) return DBNull.Value;
                        if (v is double || v is float)
                        {
                            double d;
                            return TryAsDouble(v, out d) ? (object)(-d) : DBNull.Value;
                        }
                        else
                        {
                            decimal d;
                            return TryAsDecimal(v, out d) ? (object)(-d) : DBNull.Value;
                        }

                    case UnaryOperator.Not:
                        return !ToBoolean(v);

                    default:
                        return DBNull.Value;
                }
            }
        }

        private enum BinaryOperator
        {
            Add,
            Subtract,
            Multiply,
            Divide,
            Modulo,
            Equal,
            NotEqual,
            LessThan,
            LessOrEqual,
            GreaterThan,
            GreaterOrEqual,
            And,
            Or
        }

        private sealed class BinaryNode : ExprNode
        {
            internal readonly BinaryOperator Op;
            internal readonly ExprNode Left;
            internal readonly ExprNode Right;

            internal BinaryNode(BinaryOperator op, ExprNode left, ExprNode right)
            {
                Op = op;
                Left = left;
                Right = right;
            }

            internal override object Eval(FastDataRow row, FastDataTable table)
            {
                object lv = Left.Eval(row, table);
                object rv = Right.Eval(row, table);

                switch (Op)
                {
                    case BinaryOperator.Add:
                        if (lv is string || rv is string)
                            return (FastKey.IsNull(lv) ? string.Empty : Convert.ToString(lv, Inv)) +
                                   (FastKey.IsNull(rv) ? string.Empty : Convert.ToString(rv, Inv));

                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return DBNull.Value;

                        if (lv is double || lv is float || rv is double || rv is float)
                        {
                            double ld, rd;
                            if (!TryAsDouble(lv, out ld) || !TryAsDouble(rv, out rd)) return DBNull.Value;
                            return ld + rd;
                        }
                        else
                        {
                            decimal ld, rd;
                            if (!TryAsDecimal(lv, out ld) || !TryAsDecimal(rv, out rd)) return DBNull.Value;
                            return ld + rd;
                        }

                    case BinaryOperator.Subtract:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return DBNull.Value;
                        {
                            double ld, rd;
                            if (!TryAsDouble(lv, out ld) || !TryAsDouble(rv, out rd)) return DBNull.Value;
                            return ld - rd;
                        }

                    case BinaryOperator.Multiply:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return DBNull.Value;
                        {
                            double ld, rd;
                            if (!TryAsDouble(lv, out ld) || !TryAsDouble(rv, out rd)) return DBNull.Value;
                            return ld * rd;
                        }

                    case BinaryOperator.Divide:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return DBNull.Value;
                        {
                            double ld, rd;
                            if (!TryAsDouble(lv, out ld) || !TryAsDouble(rv, out rd)) return DBNull.Value;
                            if (rd == 0d) return DBNull.Value;
                            return ld / rd;
                        }

                    case BinaryOperator.Modulo:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return DBNull.Value;
                        {
                            double ld, rd;
                            if (!TryAsDouble(lv, out ld) || !TryAsDouble(rv, out rd)) return DBNull.Value;
                            if (rd == 0d) return DBNull.Value;
                            return ld % rd;
                        }

                    case BinaryOperator.Equal:
                        if (FastKey.IsNull(lv) && FastKey.IsNull(rv)) return true;
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return false;
                        {
                            // Numeric comparison for different types (e.g. double vs int)
                            double ld, rd;
                            if (TryAsDouble(lv, out ld) && TryAsDouble(rv, out rd))
                            {
                                return ld == rd;
                            }
                        }
                        return FastObjectComparer.Instance.Equals(FastKey.Normalize(lv), FastKey.Normalize(rv));

                    case BinaryOperator.NotEqual:
                        if (FastKey.IsNull(lv) && FastKey.IsNull(rv)) return false;
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return true;
                        {
                            // Numeric comparison for different types (e.g. double vs int)
                            double ld, rd;
                            if (TryAsDouble(lv, out ld) && TryAsDouble(rv, out rd))
                            {
                                return ld != rd;
                            }
                        }
                        return !FastObjectComparer.Instance.Equals(FastKey.Normalize(lv), FastKey.Normalize(rv));

                    case BinaryOperator.LessThan:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return false;
                        {
                            // Numeric comparison for different types (e.g. double vs int)
                            double ld, rd;
                            if (TryAsDouble(lv, out ld) && TryAsDouble(rv, out rd))
                            {
                                return ld < rd;
                            }
                        }
                        return FastObjectComparer.Instance.Compare(lv, rv) < 0;

                    case BinaryOperator.LessOrEqual:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return false;
                        {
                            // Numeric comparison for different types (e.g. double vs int)
                            double ld, rd;
                            if (TryAsDouble(lv, out ld) && TryAsDouble(rv, out rd))
                            {
                                return ld <= rd;
                            }
                        }
                        return FastObjectComparer.Instance.Compare(lv, rv) <= 0;

                    case BinaryOperator.GreaterThan:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return false;
                        {
                            // Numeric comparison for different types (e.g. double vs int)
                            double ld, rd;
                            if (TryAsDouble(lv, out ld) && TryAsDouble(rv, out rd))
                            {
                                return ld > rd;
                            }
                        }
                        return FastObjectComparer.Instance.Compare(lv, rv) > 0;

                    case BinaryOperator.GreaterOrEqual:
                        if (FastKey.IsNull(lv) || FastKey.IsNull(rv)) return false;
                        {
                            // Numeric comparison for different types (e.g. double vs int)
                            double ld, rd;
                            if (TryAsDouble(lv, out ld) && TryAsDouble(rv, out rd))
                            {
                                return ld >= rd;
                            }
                        }
                        return FastObjectComparer.Instance.Compare(lv, rv) >= 0;

                    case BinaryOperator.And:
                        return ToBoolean(lv) && ToBoolean(rv);

                    case BinaryOperator.Or:
                        return ToBoolean(lv) || ToBoolean(rv);

                    default:
                        return DBNull.Value;
                }
            }
        }

        private sealed class FunctionNode : ExprNode
        {
            private readonly string _name;
            internal readonly ExprNode[] Args;

            internal FunctionNode(string name, ExprNode[] args)
            {
                _name = name;
                Args = args ?? new ExprNode[0];
            }

            internal override object Eval(FastDataRow row, FastDataTable table)
            {
                switch (_name)
                {
                    case "ISNULL":
                        if (Args.Length != 2) throw new InvalidOperationException("ISNULL richiede 2 argomenti.");
                        {
                            object a = Args[0].Eval(row, table);
                            return !FastKey.IsNull(a) ? a : Args[1].Eval(row, table);
                        }

                    case "IIF":
                        if (Args.Length != 3) throw new InvalidOperationException("IIF richiede 3 argomenti.");
                        return ToBoolean(Args[0].Eval(row, table)) ? Args[1].Eval(row, table) : Args[2].Eval(row, table);

                    case "LEN":
                        if (Args.Length != 1) throw new InvalidOperationException("LEN richiede 1 argomento.");
                        {
                            object a = Args[0].Eval(row, table);
                            return FastKey.IsNull(a) ? 0 : Convert.ToString(a, Inv).Length;
                        }

                    case "UPPER":
                        if (Args.Length != 1) throw new InvalidOperationException("UPPER richiede 1 argomento.");
                        {
                            object a = Args[0].Eval(row, table);
                            return FastKey.IsNull(a) ? (object)DBNull.Value : Convert.ToString(a, Inv).ToUpperInvariant();
                        }

                    case "LOWER":
                        if (Args.Length != 1) throw new InvalidOperationException("LOWER richiede 1 argomento.");
                        {
                            object a = Args[0].Eval(row, table);
                            return FastKey.IsNull(a) ? (object)DBNull.Value : Convert.ToString(a, Inv).ToLowerInvariant();
                        }

                    case "TRIM":
                        if (Args.Length != 1) throw new InvalidOperationException("TRIM richiede 1 argomento.");
                        {
                            object a = Args[0].Eval(row, table);
                            return FastKey.IsNull(a) ? (object)DBNull.Value : Convert.ToString(a, Inv).Trim();
                        }

                    case "ABS":
                        if (Args.Length != 1) throw new InvalidOperationException("ABS richiede 1 argomento.");
                        {
                            object a = Args[0].Eval(row, table);
                            if (FastKey.IsNull(a)) return DBNull.Value;
                            double d;
                            return TryAsDouble(a, out d) ? (object)Math.Abs(d) : DBNull.Value;
                        }

                    case "ROUND":
                        if (Args.Length != 1 && Args.Length != 2) throw new InvalidOperationException("ROUND richiede 1 o 2 argomenti.");
                        {
                            object a = Args[0].Eval(row, table);
                            if (FastKey.IsNull(a)) return DBNull.Value;

                            int digits = 0;
                            if (Args.Length == 2)
                            {
                                object b = Args[1].Eval(row, table);
                                if (!FastKey.IsNull(b)) digits = Convert.ToInt32(b, Inv);
                            }

                            double d;
                            return TryAsDouble(a, out d) ? (object)Math.Round(d, digits) : DBNull.Value;
                        }

                    case "NOW":
                        if (Args.Length != 0) throw new InvalidOperationException("NOW non accetta argomenti.");
                        return DateTime.Now;

                    case "TODAY":
                        if (Args.Length != 0) throw new InvalidOperationException("TODAY non accetta argomenti.");
                        return DateTime.Today;

                    default:
                        throw new NotSupportedException("Funzione non supportata: " + _name);
                }
            }
        }

        private sealed class ChildAggregateNode : ExprNode
        {
            private readonly AggregateFunction _fn;
            private readonly string _relationName;
            private readonly string _childColumnName;

            private FastDataSet _cachedDataSet;
            private FastDataRelation _cachedRelation;
            private int _cachedChildOrdinal = -1;

            internal ChildAggregateNode(AggregateFunction fn, string relationName, string childColumnName)
            {
                _fn = fn;
                _relationName = relationName;
                _childColumnName = childColumnName;
            }

            internal override object Eval(FastDataRow row, FastDataTable table)
            {
                FastDataRelation rel;
                int childOrd;

                if (!TryResolve(table, out rel, out childOrd))
                    return _fn == AggregateFunction.Count ? (object)0 : DBNull.Value;

                List<FastDataRow> children = rel.GetChildrenList(row);
                if (children.Count == 0)
                    return _fn == AggregateFunction.Count ? (object)0 : DBNull.Value;

                switch (_fn)
                {
                    case AggregateFunction.Count:
                        {
                            int c = 0;
                            for (int i = 0; i < children.Count; i++)
                            {
                                object v = children[i].GetValue(childOrd);
                                if (!FastKey.IsNull(v)) c++;
                            }
                            return c;
                        }

                    case AggregateFunction.Sum:
                        {
                            decimal sum = 0m;
                            bool has = false;
                            for (int i = 0; i < children.Count; i++)
                            {
                                decimal d;
                                if (!TryAsDecimal(children[i].GetValue(childOrd), out d)) continue;
                                sum += d;
                                has = true;
                            }
                            return has ? (object)sum : DBNull.Value;
                        }

                    case AggregateFunction.Avg:
                        {
                            decimal sum = 0m;
                            int n = 0;
                            for (int i = 0; i < children.Count; i++)
                            {
                                decimal d;
                                if (!TryAsDecimal(children[i].GetValue(childOrd), out d)) continue;
                                sum += d;
                                n++;
                            }
                            return n > 0 ? (object)(sum / n) : DBNull.Value;
                        }

                    case AggregateFunction.Min:
                        {
                            object min = null;
                            bool has = false;
                            for (int i = 0; i < children.Count; i++)
                            {
                                object v = children[i].GetValue(childOrd);
                                if (FastKey.IsNull(v)) continue;

                                if (!has) { min = v; has = true; }
                                else if (FastObjectComparer.Instance.Compare(v, min) < 0) min = v;
                            }
                            return has ? min : DBNull.Value;
                        }

                    case AggregateFunction.Max:
                        {
                            object max = null;
                            bool has = false;
                            for (int i = 0; i < children.Count; i++)
                            {
                                object v = children[i].GetValue(childOrd);
                                if (FastKey.IsNull(v)) continue;

                                if (!has) { max = v; has = true; }
                                else if (FastObjectComparer.Instance.Compare(v, max) > 0) max = v;
                            }
                            return has ? max : DBNull.Value;
                        }

                    default:
                        return DBNull.Value;
                }
            }

            private bool TryResolve(FastDataTable table, out FastDataRelation rel, out int childOrd)
            {
                rel = null;
                childOrd = -1;

                FastDataSet ds = table.DataSet;
                if (ds == null) return false;

                if (ReferenceEquals(ds, _cachedDataSet) && _cachedRelation != null && _cachedChildOrdinal >= 0)
                {
                    rel = _cachedRelation;
                    childOrd = _cachedChildOrdinal;
                    return true;
                }

                rel = ds.Relations[_relationName];
                if (rel == null) return false;

                if (!ReferenceEquals(rel.ParentTable, table))
                    throw new InvalidOperationException("Relation '" + _relationName + "' non ha ParentTable '" + table.TableName + "'.");

                if (!rel.ChildTable.Columns.Contains(_childColumnName))
                    throw new InvalidOperationException("Colonna child '" + _childColumnName + "' non esiste in '" + rel.ChildTable.TableName + "'.");

                childOrd = rel.ChildTable.Columns.GetOrdinal(_childColumnName);

                _cachedDataSet = ds;
                _cachedRelation = rel;
                _cachedChildOrdinal = childOrd;
                return true;
            }
        }

        #endregion

        #region Parser  

        private sealed class Parser
        {
            private readonly FastDataTable _table;
            private readonly Tokenizer _tz;
            private Token _current;

            internal readonly HashSet<int> ColumnDependencies = new HashSet<int>();
            internal readonly List<ChildAggregateRef> ChildAggregates = new List<ChildAggregateRef>();

            internal Parser(string expression, FastDataTable table)
            {
                _table = table;
                _tz = new Tokenizer(expression ?? string.Empty);
                _current = _tz.Next();
            }

            internal ExprNode ParseExpression()
            {
                return ParseOr();
            }

            internal void EnsureEnd()
            {
                if (_current.Kind != TokenKind.End)
                    throw new InvalidOperationException("Token inatteso: '" + _current.Text + "'.");
            }

            private ExprNode ParseOr()
            {
                ExprNode left = ParseAnd();
                while (IsIdentifier("OR"))
                {
                    Next();
                    left = new BinaryNode(BinaryOperator.Or, left, ParseAnd());
                }
                return left;
            }

            private ExprNode ParseAnd()
            {
                ExprNode left = ParseCompare();
                while (IsIdentifier("AND"))
                {
                    Next();
                    left = new BinaryNode(BinaryOperator.And, left, ParseCompare());
                }
                return left;
            }

            private ExprNode ParseCompare()
            {
                ExprNode left = ParseAddSub();

                //while (true)
                //{
                //    if (_current.Kind == TokenKind.Eq) { Next(); left = new BinaryNode(BinaryOperator.Equal, left, ParseAddSub()); }
                //    else if (_current.Kind == TokenKind.Ne) { Next(); left = new BinaryNode(BinaryOperator.NotEqual, left, ParseAddSub()); }
                //    else if (_current.Kind == TokenKind.Lt) { Next(); left = new BinaryNode(BinaryOperator.LessThan, left, ParseAddSub()); }
                //    else if (_current.Kind == TokenKind.Le) { Next(); left = new BinaryNode(BinaryOperator.LessOrEqual, left, ParseAddSub()); }
                //    else if (_current.Kind == TokenKind.Gt) { Next(); left = new BinaryNode(BinaryOperator.GreaterThan, left, ParseAddSub()); }
                //    else if (_current.Kind == TokenKind.Ge) { Next(); left = new BinaryNode(BinaryOperator.GreaterOrEqual, left, ParseAddSub()); }
                //    else break;
                //}
                while (true)
                {
                    if (_current.Kind == TokenKind.Eq) { Next(); left = new BinaryNode(BinaryOperator.Equal, left, ParseAddSub()); }
                    else if (_current.Kind == TokenKind.Ne) { Next(); left = new BinaryNode(BinaryOperator.NotEqual, left, ParseAddSub()); }
                    else if (_current.Kind == TokenKind.Lt) { Next(); left = new BinaryNode(BinaryOperator.LessThan, left, ParseAddSub()); }
                    else if (_current.Kind == TokenKind.Le) { Next(); left = new BinaryNode(BinaryOperator.LessOrEqual, left, ParseAddSub()); }
                    else if (_current.Kind == TokenKind.Gt) { Next(); left = new BinaryNode(BinaryOperator.GreaterThan, left, ParseAddSub()); }
                    else if (_current.Kind == TokenKind.Ge) { Next(); left = new BinaryNode(BinaryOperator.GreaterOrEqual, left, ParseAddSub()); }
                    else if (IsIdentifier("IS"))
                    {
                        Next();
                        if (IsIdentifier("NULL"))
                        {
                            Next();
                            left = new BinaryNode(BinaryOperator.Equal, left, new ConstantNode(DBNull.Value));
                        }
                        else
                        {
                            throw new InvalidOperationException("Token inatteso dopo IS. Atteso NULL.");
                        }
                    }
                    else break;
                }

                return left;
            }

            private ExprNode ParseAddSub()
            {
                ExprNode left = ParseMulDiv();

                while (true)
                {
                    if (_current.Kind == TokenKind.Plus) { Next(); left = new BinaryNode(BinaryOperator.Add, left, ParseMulDiv()); }
                    else if (_current.Kind == TokenKind.Minus) { Next(); left = new BinaryNode(BinaryOperator.Subtract, left, ParseMulDiv()); }
                    else break;
                }

                return left;
            }

            private ExprNode ParseMulDiv()
            {
                ExprNode left = ParseUnary();

                while (true)
                {
                    if (_current.Kind == TokenKind.Star) { Next(); left = new BinaryNode(BinaryOperator.Multiply, left, ParseUnary()); }
                    else if (_current.Kind == TokenKind.Slash) { Next(); left = new BinaryNode(BinaryOperator.Divide, left, ParseUnary()); }
                    else if (_current.Kind == TokenKind.Percent) { Next(); left = new BinaryNode(BinaryOperator.Modulo, left, ParseUnary()); }
                    else break;
                }

                return left;
            }

            private ExprNode ParseUnary()
            {
                if (_current.Kind == TokenKind.Plus) { Next(); return new UnaryNode(UnaryOperator.Plus, ParseUnary()); }
                if (_current.Kind == TokenKind.Minus) { Next(); return new UnaryNode(UnaryOperator.Minus, ParseUnary()); }
                if (IsIdentifier("NOT")) { Next(); return new UnaryNode(UnaryOperator.Not, ParseUnary()); }
                return ParsePrimary();
            }

            private ExprNode ParsePrimary()
            {
                if (_current.Kind == TokenKind.Number)
                {
                    string n = _current.Text;
                    Next();
                    return new ConstantNode(ParseNumber(n));
                }

                if (_current.Kind == TokenKind.String)
                {
                    string s = _current.Text;
                    Next();
                    return new ConstantNode(s);
                }

                if (_current.Kind == TokenKind.Identifier)
                {
                    string id = _current.Text;
                    Next();

                    if (string.Equals(id, "NULL", StringComparison.OrdinalIgnoreCase)) return new ConstantNode(DBNull.Value);
                    if (string.Equals(id, "TRUE", StringComparison.OrdinalIgnoreCase)) return new ConstantNode(true);
                    if (string.Equals(id, "FALSE", StringComparison.OrdinalIgnoreCase)) return new ConstantNode(false);

                    if (string.Equals(id, "Child", StringComparison.OrdinalIgnoreCase) && _current.Kind == TokenKind.LParen)
                        return ParseChildAccessor();

                    if (_current.Kind == TokenKind.LParen)
                        return ParseFunction(id);

                    return ParseColumn(id);
                }

                if (_current.Kind == TokenKind.LParen)
                {
                    Next();
                    ExprNode n = ParseExpression();
                    Expect(TokenKind.RParen);
                    return n;
                }

                throw new InvalidOperationException("Token inatteso: '" + _current.Text + "'.");
            }

            private ExprNode ParseChildAccessor()
            {
                Expect(TokenKind.LParen);
                string relation = ParseIdentifierLike("relation name");
                Expect(TokenKind.RParen);
                Expect(TokenKind.Dot);
                string childCol = ParseIdentifierLike("child column");
                return new ChildColumnRefNode(relation, childCol);
            }

            private ExprNode ParseFunction(string functionName)
            {
                string fn = functionName.ToUpperInvariant();

                if (!IsSupportedFunction(fn) && !IsAggregate(fn))
                    throw new NotSupportedException("Funzione non supportata: " + functionName);

                Expect(TokenKind.LParen);

                List<ExprNode> args = new List<ExprNode>(2);
                if (_current.Kind != TokenKind.RParen)
                {
                    while (true)
                    {
                        args.Add(ParseExpression());
                        if (_current.Kind == TokenKind.Comma) { Next(); continue; }
                        break;
                    }
                }

                Expect(TokenKind.RParen);

                if (IsAggregate(fn))
                {
                    if (args.Count != 1)
                        throw new InvalidOperationException(fn + " richiede 1 argomento (Child(Rel).Col).");

                    ChildColumnRefNode child = args[0] as ChildColumnRefNode;
                    if (child == null)
                        throw new InvalidOperationException(fn + " supporta solo Child(Rel).Col.");

                    AggregateFunction af = ParseAggregate(fn);

                    ChildAggregates.Add(new ChildAggregateRef
                    {
                        Function = af,
                        RelationName = child.RelationName,
                        ChildColumnName = child.ColumnName
                    });

                    return new ChildAggregateNode(af, child.RelationName, child.ColumnName);
                }

                return new FunctionNode(fn, args.ToArray());
            }

            private ExprNode ParseColumn(string colName)
            {
                if (!_table.Columns.Contains(colName))
                    throw new InvalidOperationException("Colonna non trovata: " + colName);

                int ord = _table.Columns.GetOrdinal(colName);
                ColumnDependencies.Add(ord);
                return new ColumnNode(ord);
            }

            private string ParseIdentifierLike(string label)
            {
                if (_current.Kind == TokenKind.Identifier || _current.Kind == TokenKind.String)
                {
                    string s = _current.Text;
                    Next();
                    return s;
                }

                throw new InvalidOperationException("Atteso " + label + ".");
            }

            private bool IsIdentifier(string token)
            {
                return _current.Kind == TokenKind.Identifier &&
                       string.Equals(_current.Text, token, StringComparison.OrdinalIgnoreCase);
            }

            private void Expect(TokenKind kind)
            {
                if (_current.Kind != kind)
                    throw new InvalidOperationException("Atteso token " + kind + " vicino a '" + _current.Text + "'.");
                Next();
            }

            private void Next()
            {
                _current = _tz.Next();
            }

            private static bool IsSupportedFunction(string fn)
            {
                return fn == "ISNULL" || fn == "IIF" || fn == "LEN" || fn == "UPPER" ||
                       fn == "LOWER" || fn == "TRIM" || fn == "ABS" || fn == "ROUND" ||
                       fn == "NOW" || fn == "TODAY";
            }

            private static bool IsAggregate(string fn)
            {
                return fn == "SUM" || fn == "AVG" || fn == "MIN" || fn == "MAX" || fn == "COUNT";
            }

            private static AggregateFunction ParseAggregate(string fn)
            {
                if (fn == "SUM") return AggregateFunction.Sum;
                if (fn == "AVG") return AggregateFunction.Avg;
                if (fn == "MIN") return AggregateFunction.Min;
                if (fn == "MAX") return AggregateFunction.Max;
                return AggregateFunction.Count;
            }

            private static object ParseNumber(string raw)
            {
                double d;
                if ((raw.IndexOf('.') >= 0 || raw.IndexOf('e') >= 0 || raw.IndexOf('E') >= 0) &&
                    double.TryParse(raw, NumberStyles.Any, Inv, out d))
                    return d;

                decimal m;
                if (decimal.TryParse(raw, NumberStyles.Any, Inv, out m))
                    return m;

                if (double.TryParse(raw, NumberStyles.Any, Inv, out d))
                    return d;

                throw new InvalidOperationException("Numero non valido: " + raw);
            }
        }

        private enum TokenKind
        {
            End,
            Identifier,
            Number,
            String,
            LParen,
            RParen,
            Comma,
            Dot,
            Plus,
            Minus,
            Star,
            Slash,
            Percent,
            Eq,
            Ne,
            Lt,
            Le,
            Gt,
            Ge
        }

        private struct Token
        {
            internal TokenKind Kind;
            internal string Text;
        }

        private sealed class Tokenizer
        {
            private readonly string _s;
            private int _i;

            internal Tokenizer(string s)
            {
                _s = s ?? string.Empty;
            }

            internal Token Next()
            {
                SkipWs();
                if (_i >= _s.Length) return new Token { Kind = TokenKind.End, Text = string.Empty };

                char ch = _s[_i];

                switch (ch)
                {
                    case '(': _i++; return new Token { Kind = TokenKind.LParen, Text = "(" };
                    case ')': _i++; return new Token { Kind = TokenKind.RParen, Text = ")" };
                    case ',': _i++; return new Token { Kind = TokenKind.Comma, Text = "," };
                    case '.':
                        if (_i + 1 < _s.Length && char.IsDigit(_s[_i + 1])) return ReadNumber();
                        _i++;
                        return new Token { Kind = TokenKind.Dot, Text = "." };
                    case '+': _i++; return new Token { Kind = TokenKind.Plus, Text = "+" };
                    case '-': _i++; return new Token { Kind = TokenKind.Minus, Text = "-" };
                    case '*': _i++; return new Token { Kind = TokenKind.Star, Text = "*" };
                    case '/': _i++; return new Token { Kind = TokenKind.Slash, Text = "/" };
                    case '%': _i++; return new Token { Kind = TokenKind.Percent, Text = "%" };
                    case '=': _i++; return new Token { Kind = TokenKind.Eq, Text = "=" };
                    case '<':
                        if (_i + 1 < _s.Length && _s[_i + 1] == '=') { _i += 2; return new Token { Kind = TokenKind.Le, Text = "<=" }; }
                        if (_i + 1 < _s.Length && _s[_i + 1] == '>') { _i += 2; return new Token { Kind = TokenKind.Ne, Text = "<>" }; }
                        _i++;
                        return new Token { Kind = TokenKind.Lt, Text = "<" };
                    case '>':
                        if (_i + 1 < _s.Length && _s[_i + 1] == '=') { _i += 2; return new Token { Kind = TokenKind.Ge, Text = ">=" }; }
                        _i++;
                        return new Token { Kind = TokenKind.Gt, Text = ">" };
                    case '\'':
                        return ReadString();
                    case '[':
                        return ReadBracketIdentifier();
                    default:
                        if (char.IsDigit(ch)) return ReadNumber();
                        if (IsIdentStart(ch)) return ReadIdentifier();
                        throw new InvalidOperationException("Carattere non valido: '" + ch + "'.");
                }
            }

            private Token ReadString()
            {
                _i++; // '  
                System.Text.StringBuilder sb = new System.Text.StringBuilder();

                while (_i < _s.Length)
                {
                    char ch = _s[_i++];
                    if (ch == '\'')
                    {
                        if (_i < _s.Length && _s[_i] == '\'')
                        {
                            _i++;
                            sb.Append('\'');
                            continue;
                        }

                        return new Token { Kind = TokenKind.String, Text = sb.ToString() };
                    }

                    sb.Append(ch);
                }

                throw new InvalidOperationException("Stringa non terminata.");
            }

            private Token ReadBracketIdentifier()
            {
                _i++; // [  
                System.Text.StringBuilder sb = new System.Text.StringBuilder();

                while (_i < _s.Length)
                {
                    char ch = _s[_i++];
                    if (ch == ']')
                    {
                        if (_i < _s.Length && _s[_i] == ']')
                        {
                            _i++;
                            sb.Append(']');
                            continue;
                        }

                        return new Token { Kind = TokenKind.Identifier, Text = sb.ToString() };
                    }

                    sb.Append(ch);
                }

                throw new InvalidOperationException("Identificatore [..] non terminato.");
            }

            private Token ReadNumber()
            {
                int start = _i;
                bool dot = false;
                bool exp = false;

                while (_i < _s.Length)
                {
                    char ch = _s[_i];
                    if (char.IsDigit(ch)) { _i++; continue; }

                    if (!dot && ch == '.') { dot = true; _i++; continue; }

                    if (!exp && (ch == 'e' || ch == 'E'))
                    {
                        exp = true;
                        _i++;
                        if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                        continue;
                    }

                    break;
                }

                return new Token { Kind = TokenKind.Number, Text = _s.Substring(start, _i - start) };
            }

            private Token ReadIdentifier()
            {
                int start = _i;
                _i++;

                while (_i < _s.Length && IsIdentPart(_s[_i]))
                    _i++;

                return new Token { Kind = TokenKind.Identifier, Text = _s.Substring(start, _i - start) };
            }

            private void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private static bool IsIdentStart(char ch)
            {
                return char.IsLetter(ch) || ch == '_' || ch == '$';
            }

            private static bool IsIdentPart(char ch)
            {
                return char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';
            }
        }

        #endregion
    }
}