using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DataColumn = RCM.FastData.FastDataColumn;
using DataRow = RCM.FastData.FastDataRow;
using DataSet = RCM.FastData.FastDataSet;
using DataTable = RCM.FastData.FastDataTable;
using DataView = RCM.FastData.FastDataView;

namespace RCM.FastData
{
    [Flags]
    public enum DataRowState
    {
        Detached = 1,
        Unchanged = 2,
        Added = 4,
        Deleted = 8,
        Modified = 16
    }

    internal static class FastKey
    {
        internal static readonly object Null = new object();

        internal static object Normalize(object value)
        {
            return value == null || value == DBNull.Value ? Null : value;
        }

        internal static bool IsNull(object value)
        {
            return value == null || value == DBNull.Value;
        }
    }

    internal static class FastNumeric
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

        internal static bool TryAsDouble(object value, out double d)
        {
            d = 0d;
            if (value == null || value == DBNull.Value) return false;

            // Native numeric types (fast path)  
            if (value is double) { d = (double)value; return true; }
            if (value is float) { d = (float)value; return true; }
            if (value is decimal) { d = (double)(decimal)value; return true; }
            if (value is int) { d = (int)value; return true; }
            if (value is long) { d = (long)value; return true; }
            if (value is short) { d = (short)value; return true; }
            if (value is byte) { d = (byte)value; return true; }
            if (value is uint) { d = (uint)value; return true; }
            if (value is ulong) { d = (ulong)value; return true; }
            if (value is bool) { d = (bool)value ? 1d : 0d; return true; }

            string s = Convert.ToString(value, CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) { d = 1d; return true; }
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) { d = 0d; return true; }

            bool hasComma = s.IndexOf(',') >= 0;
            bool hasDot = s.IndexOf('.') >= 0;

            // aligned with literal parsing: comma -> try it/current/invariant  
            if (hasComma && !hasDot)
            {
                if (double.TryParse(s, NumberStyles.Any, It, out d)) return true;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d)) return true;
                if (double.TryParse(s, NumberStyles.Any, Inv, out d)) return true;

                string normalized = s.Replace(',', '.');
                return double.TryParse(normalized, NumberStyles.Any, Inv, out d);
            }

            // dot -> try invariant/current/it  
            if (hasDot && !hasComma)
            {
                if (double.TryParse(s, NumberStyles.Any, Inv, out d)) return true;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d)) return true;
                return double.TryParse(s, NumberStyles.Any, It, out d);
            }

            // mixed or no separators  
            if (double.TryParse(s, NumberStyles.Any, Inv, out d)) return true;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d)) return true;
            if (double.TryParse(s, NumberStyles.Any, It, out d)) return true;

            string mixed = NormalizeMixedSeparators(s);
            if (!string.Equals(mixed, s, StringComparison.Ordinal))
                if (double.TryParse(mixed, NumberStyles.Any, Inv, out d)) return true;

            return false;
        }

        private static string NormalizeMixedSeparators(string s)
        {
            int lastComma = s.LastIndexOf(',');
            int lastDot = s.LastIndexOf('.');

            if (lastComma >= 0 && lastDot >= 0)
            {
                // 1,234.56 -> 1234.56  
                if (lastDot > lastComma) return s.Replace(",", "");
                // 1.234,56 -> 1234.56  
                return s.Replace(".", "").Replace(',', '.');
            }

            return s;
        }
    }
    internal sealed class CompositeKey : IEquatable<CompositeKey>
    {
        private readonly object[] _values;
        private readonly int _hash;

        internal CompositeKey(object[] values)
        {
            _values = values ?? new object[0];
            unchecked
            {
                int h = 17;
                for (int i = 0; i < _values.Length; i++)
                    h = (h * 31) + FastObjectComparer.Instance.GetHashCode(FastKey.Normalize(_values[i]));
                _hash = h;
            }
        }

        public bool Equals(CompositeKey other)
        {
            if (other == null || other._values.Length != _values.Length) return false;

            for (int i = 0; i < _values.Length; i++)
            {
                if (!FastObjectComparer.Instance.Equals(
                        FastKey.Normalize(_values[i]),
                        FastKey.Normalize(other._values[i])))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as CompositeKey);
        }

        public override int GetHashCode()
        {
            return _hash;
        }
    }

    internal sealed class FastObjectComparer : IEqualityComparer<object>, IComparer<object>
    {
        internal static readonly FastObjectComparer Instance = new FastObjectComparer();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private FastObjectComparer() { }

        public new bool Equals(object x, object y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            CompositeKey cx = x as CompositeKey;
            if (cx != null)
            {
                CompositeKey cy = y as CompositeKey;
                return cy != null && cx.Equals(cy);
            }

            string sx = x as string;
            string sy = y as string;
            if (sx != null && sy != null)
                return string.Equals(sx, sy, StringComparison.OrdinalIgnoreCase);

            return x.Equals(y);
        }

        public int GetHashCode(object obj)
        {
            if (obj == null) return 0;

            CompositeKey ck = obj as CompositeKey;
            if (ck != null) return ck.GetHashCode();

            string s = obj as string;
            if (s != null) return StringComparer.OrdinalIgnoreCase.GetHashCode(s);

            return obj.GetHashCode();
        }

        public int Compare(object x, object y)
        {
            bool xNull = FastKey.IsNull(x);
            bool yNull = FastKey.IsNull(y);
            if (xNull && yNull) return 0;
            if (xNull) return -1;
            if (yNull) return 1;

            string sx = x as string;
            string sy = y as string;
            if (sx != null && sy != null)
                return string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);

            if (x.GetType() == y.GetType())
            {
                IComparable c = x as IComparable;
                if (c != null) return c.CompareTo(y);
            }

            double dx, dy;
            if (TryAsDouble(x, out dx) && TryAsDouble(y, out dy))
                return dx.CompareTo(dy);

            return string.CompareOrdinal(x.ToString(), y.ToString());
        }

        //private static bool TryAsDouble(object value, out double d)
        //{
        //    if (value is double) { d = (double)value; return true; }
        //    if (value is float) { d = (float)value; return true; }
        //    if (value is decimal) { d = (double)(decimal)value; return true; }
        //    if (value is int) { d = (int)value; return true; }
        //    if (value is long) { d = (long)value; return true; }

        //    return double.TryParse(Convert.ToString(value, Inv), NumberStyles.Any, Inv, out d);
        //}
        private static bool TryAsDouble(object value, out double d)
        {
            return FastNumeric.TryAsDouble(value, out d);
        }
    }

    public sealed class FastDataColumn
    {
        private string _columnName;
        private Type _dataType;
        private string _expression = string.Empty;

        public FastDataColumn() : this(string.Empty, typeof(string)) { }
        public FastDataColumn(string columnName) : this(columnName, typeof(string)) { }

        public FastDataColumn(string columnName, Type dataType)
        {
            _columnName = columnName ?? string.Empty;
            _dataType = dataType ?? typeof(string);
        }

        public string ColumnName
        {
            get { return _columnName; }
            set
            {
                if (Table != null)
                    throw new InvalidOperationException("Cannot rename a column already attached to a table.");

                _columnName = value ?? string.Empty;
            }
        }

        public Type DataType
        {
            get { return _dataType; }
            set
            {
                if (Table != null)
                    throw new InvalidOperationException("Cannot change DataType on an attached column.");

                _dataType = value ?? typeof(string);
            }
        }

        public string Expression
        {
            get { return _expression; }
            set
            {
                string expr = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
                if (string.Equals(_expression, expr, StringComparison.Ordinal)) return;

                string old = _expression;
                _expression = expr;

                if (Table != null)
                {
                    try
                    {
                        Table.OnColumnExpressionChanged(this);
                    }
                    catch
                    {
                        _expression = old;
                        throw;
                    }
                }
            }
        }

        internal bool HasExpression
        {
            get { return _expression.Length > 0; }
        }

        public int Ordinal { get; internal set; }
        internal FastDataTable Table { get; set; }
    }

    public sealed class FastDataColumnCollection : IEnumerable<FastDataColumn>
    {
        private readonly FastDataTable _table;
        internal readonly List<FastDataColumn> InternalList;
        private readonly Dictionary<string, FastDataColumn> _byName;

        internal FastDataColumnCollection(FastDataTable table)
        {
            _table = table;
            InternalList = new List<FastDataColumn>(8);
            _byName = new Dictionary<string, FastDataColumn>(StringComparer.OrdinalIgnoreCase);
        }

        public int Count { get { return InternalList.Count; } }

        public FastDataColumn this[int index] { get { return InternalList[index]; } }

        public FastDataColumn this[string columnName]
        {
            get
            {
                FastDataColumn col;
                if (!_byName.TryGetValue(columnName, out col))
                    throw new ArgumentException("Column not found: " + columnName);

                return col;
            }
        }

        public bool Contains(string columnName)
        {
            return _byName.ContainsKey(columnName);
        }

        public FastDataColumn Add(string columnName, Type dataType)
        {
            FastDataColumn col = new FastDataColumn(columnName, dataType);
            Add(col);
            return col;
        }

        public FastDataColumn Add(string columnName)
        {
            FastDataColumn col = new FastDataColumn(columnName, Type.GetType("System.String"));
            Add(col);
            return col;
        }

        public void Add(FastDataColumn column)
        {
            if (column == null) throw new ArgumentNullException("column");
            if (string.IsNullOrWhiteSpace(column.ColumnName))
                throw new ArgumentException("ColumnName is required.");
            if (_byName.ContainsKey(column.ColumnName))
                throw new ArgumentException("Column already exists: " + column.ColumnName);

            column.Table = _table;
            column.Ordinal = InternalList.Count;
            InternalList.Add(column);
            _byName.Add(column.ColumnName, column);

            _table.OnColumnAdded(column);
        }

        internal int GetOrdinal(string columnName)
        {
            return this[columnName].Ordinal;
        }

        public IEnumerator<FastDataColumn> GetEnumerator()
        {
            return InternalList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public sealed class FastDataRow
    {
        private static readonly FastDataRow[] EmptyRows = new FastDataRow[0];
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal object[] _values;

        internal FastDataRow(FastDataTable table)
        {
            if (table == null) throw new ArgumentNullException("table");

            Table = table;
            _values = new object[table.Columns.Count];
            for (int i = 0; i < _values.Length; i++) _values[i] = DBNull.Value;
            RowState = DataRowState.Detached;
        }

        public FastDataTable Table { get; private set; }
        public DataRowState RowState { get; internal set; }

        public object this[int ordinal]
        {
            get { return _values[ordinal]; }
            set { SetValue(ordinal, value); }
        }

        public object this[string columnName]
        {
            get { return _values[Table.Columns.GetOrdinal(columnName)]; }
            set { SetValue(Table.Columns.GetOrdinal(columnName), value); }
        }

        public object[] ItemArray
        {
            get
            {
                object[] copy = new object[_values.Length];
                Array.Copy(_values, copy, _values.Length);
                return copy;
            }
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                if (value.Length != _values.Length)
                    throw new ArgumentException("ItemArray length mismatch.");

                for (int i = 0; i < value.Length; i++) SetValue(i, value[i]);
            }
        }

        internal object GetValue(int ordinal)
        {
            return _values[ordinal];
        }

        internal void Expand(int newSize)
        {
            int oldSize = _values.Length;
            if (newSize <= oldSize) return;

            Array.Resize(ref _values, newSize);
            for (int i = oldSize; i < newSize; i++) _values[i] = DBNull.Value;
        }

        private void SetValue(int ordinal, object value)
        {
            SetValueCore(ordinal, value, false);
        }

        internal void SetCalculatedValue(int ordinal, object value)
        {
            SetValueCore(ordinal, value, true);
        }

        private void SetValueCore(int ordinal, object value, bool isCalculatedWrite)
        {
            if (!isCalculatedWrite && Table.Columns[ordinal].HasExpression)
                throw new InvalidOperationException("Cannot set value on an expression column: " + Table.Columns[ordinal].ColumnName);

            if (RowState == DataRowState.Deleted)
                throw new InvalidOperationException("Cannot edit a deleted row.");

            object newValue = value ?? DBNull.Value;

            // D11: assignment with conversion to the column type, like System.Data
            // (Convert.ChangeType with the "current" culture: 1.5d -> int = 2, "123" -> 123,
            // "non-numeric" -> exception). Fast path without Convert if the type is already
            // that of the column (loading hot path).
            if (!FastKey.IsNull(newValue))
            {
                Type baseColType = Table.Columns[ordinal].DataType;
                baseColType = Nullable.GetUnderlyingType(baseColType) ?? baseColType;
                if (baseColType != typeof(object) && newValue.GetType() != baseColType)
                    newValue = Convert.ChangeType(newValue, baseColType, CultureInfo.CurrentCulture);
            }

            // D12: foreign key constraint (createConstraints) - validated BEFORE the
            // write, so that a failure does not leave partially modified state.
            if (!Table.IsLoading)
                Table.ValidateChildKeyWrite(this, ordinal, newValue);

            object oldValue = _values[ordinal];

            // D6: change detection with ORDINAL (case-sensitive) parity - a case-only
            // difference ("Roma" -> "ROMA") is a real change (state, indexes and
            // expressions are updated), as in System.Data.
            if (SameValue(oldValue, newValue)) return;

            if (RowState != DataRowState.Detached)
                Table.OnRowValueChanging(this, ordinal, oldValue, newValue);

            _values[ordinal] = newValue;

            if (RowState != DataRowState.Detached)
                Table.OnRowValueChanged(this, ordinal, oldValue, newValue);

            if (!isCalculatedWrite && RowState == DataRowState.Unchanged)
                RowState = DataRowState.Modified;
        }

        // D6: equality for change detection. DBNull == DBNull; strings with ordinal
        // parity (case-sensitive); other types with Equals.
        private static bool SameValue(object a, object b)
        {
            if (a == null) a = DBNull.Value;
            if (b == null) b = DBNull.Value;
            if (a == DBNull.Value) return b == DBNull.Value;
            if (b == DBNull.Value) return false;
            return a.Equals(b);
        }

        public void Delete()
        {
            if (RowState == DataRowState.Detached)
                throw new InvalidOperationException("Row is detached.");
            if (RowState == DataRowState.Deleted) return;

            // D12: deletion constraint - a parent with children (createConstraints)
            // cannot be deleted. Validated BEFORE the state change (no rollback).
            Table.ValidateParentDelete(this);

            // D7: an Added row (not yet persisted), when deleted, is removed
            // IMMEDIATELY as in System.Data: Count drops and the state becomes Detached.
            if (RowState == DataRowState.Added)
            {
                RowState = DataRowState.Detached;
                Table.OnRowDeleted(this);
                Table.DetachAddedRow(this);
                return;
            }

            RowState = DataRowState.Deleted;
            Table.OnRowDeleted(this);
        }

        public FastDataRow[] GetChildRows(string relationName)
        {
            if (Table.DataSet == null) return EmptyRows;
            return Table.DataSet.Relations.GetChildren(relationName, this);
        }

        public T Field<T>(string columnName)
        {
            return Field<T>(Table.Columns.GetOrdinal(columnName));
        }

        public T Field<T>(int ordinal)
        {
            object value = _values[ordinal];

            // D10: strict unboxing, identical to System.Data's Field<T>:
            //   NULL  -> null only if T = Nullable<T>, otherwise InvalidCastException;
            //   non NULL -> the runtime type must be EXACTLY the target type
            //   (no conversion: Field<double> on int, Field<string> on int -> ICE).
            if (FastKey.IsNull(value))
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) != null)
                    return default(T);
                throw new InvalidCastException("Valore NULL non convertibile in " + typeof(T).Name + ".");
            }

            Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (value.GetType() != target)
                throw new InvalidCastException("Valore di tipo " + value.GetType().Name +
                    " non convertibile in " + target.Name + ".");

            return (T)value;
        }
    }

    public sealed class FastDataRowCollection : IEnumerable<FastDataRow>
    {
        private readonly FastDataTable _table;
        internal readonly List<FastDataRow> InternalList;

        internal FastDataRowCollection(FastDataTable table)
        {
            _table = table;
            InternalList = new List<FastDataRow>(16);
        }

        public int Count { get { return InternalList.Count; } }

        public FastDataRow this[int index] { get { return InternalList[index]; } }

        public void Add(FastDataRow row)
        {
            // D12: FK validation BEFORE the attach (no rollback if it fails).
            _table.ValidateChildRowAdd(row);
            _table.AttachRow(row);
        }

        public FastDataRow Add(params object[] values)
        {
            FastDataRow r = _table.NewRow();
            if (values != null)
            {
                int n = Math.Min(values.Length, _table.Columns.Count);
                for (int i = 0; i < n; i++)
                {
                    if (_table.Columns[i].HasExpression) continue;
                    r[i] = values[i];
                }
            }

            Add(r);
            return r;
        }

        public void Clear()
        {
            _table.ClearRows();
        }

        // P1: System.Data parity - point lookup by primary key.
        public FastDataRow Find(object key)
        {
            return Find(new object[] { key });
        }

        public FastDataRow Find(params object[] keys)
        {
            FastDataColumn[] pk = _table.PrimaryKey;
            if (pk == null || pk.Length == 0)
                throw new InvalidOperationException("Find richiede una chiave primaria sulla tabella.");
            if (keys == null) throw new ArgumentNullException("keys");
            if (keys.Length != pk.Length)
                throw new ArgumentException("keys.Length deve coincidere con il numero di colonne della chiave primaria.");

            List<FastDataRow> bucket = _table.FindRowsByOrdinalValue(pk[0].Ordinal, keys[0]);
            if (bucket == null || bucket.Count == 0) return null;

            if (pk.Length == 1) return bucket[0];

            // composite key: filters the bucket on the remaining columns.
            for (int i = 0; i < bucket.Count; i++)
            {
                FastDataRow r = bucket[i];
                bool all = true;
                for (int k = 1; k < pk.Length; k++)
                {
                    if (!FastObjectComparer.Instance.Equals(
                        FastKey.Normalize(r.GetValue(pk[k].Ordinal)), FastKey.Normalize(keys[k])))
                    { all = false; break; }
                }
                if (all) return r;
            }

            return null;
        }

        public bool Contains(object key)
        {
            return Contains(new object[] { key });
        }

        public bool Contains(params object[] keys)
        {
            return Find(keys) != null;
        }

        // P1: System.Data parity - IMMEDIATE removal of the row (as opposed to
        // Delete, which marks Deleted and leaves it until AcceptChanges).
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= InternalList.Count)
                throw new ArgumentOutOfRangeException("index");

            FastDataRow row = InternalList[index];
            if (row.RowState == DataRowState.Detached)
                throw new InvalidOperationException("La riga e' gia' staccata.");

            row.RowState = DataRowState.Detached;
            _table.OnRowDeleted(row);
            InternalList.RemoveAt(index);
        }

        internal void EnsureCapacity(int capacity)
        {
            if (InternalList.Capacity < capacity)
                InternalList.Capacity = capacity;
        }

        public IEnumerator<FastDataRow> GetEnumerator()
        {
            return InternalList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public sealed class FastDataTable : IEnumerable<FastDataRow>
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly FastDataColumnCollection _columns;
        private readonly FastDataRowCollection _rows;
        private readonly Dictionary<int, Dictionary<object, List<FastDataRow>>> _indexes;

        private FastDataColumn[] _primaryKey;
        private DataView _defaultView;
        private bool _isLoading;
        private int _minimumCapacity;
        private string _tableName;

        // Expression engine (interpreted AST, FastExpressionEngine).
        // The "compiled delegates" fields were dead (never written nor read): removed.
        private readonly FastExpressionEngine _expressionEngine;
        private bool _isRecalculatingExpressions;
        internal FastDataSet DataSet { get; set; }
        private enum FilterKind
        {
            Equal,
            IsNull,
            IsNotNull,
            Like,
            LessThan,
            LessThanOrEqual,
            GreaterThan,
            GreaterThanOrEqual,
            NotEqual
        }

        private struct FilterClause
        {
            public int Ordinal;
            public FilterKind Kind;
            public object Value;
            public bool IsPattern; // True if the value is a LIKE pattern (contains % or _)
            public bool UseIsNullFunction;
            public object IsNullFallback;
        }

        private struct SortClause
        {
            public int Ordinal;
            public bool Desc;
        }

        private enum FilterNodeType
        {
            Clause,
            And,
            Or,
            Not
        }

        private sealed class FilterNode
        {
            public FilterNodeType Type;
            public FilterClause Clause;
            public FilterNode Left;
            public FilterNode Right;
        }

        // D3: square brackets must be escaped (\[ and \]); the corrupted form
        // "$[^$]+\]" never matched and filters on columns like [Con Spazio]
        // were rejected. The ISNULL(...) parser is manual (TryParseAtomicIsNullClause),
        // the regex of the same name was dead and has been removed.
        private static readonly Regex RxIsNull = new Regex(
            @"^\s*(?<col>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s+IS\s+(?<not>NOT\s+)?NULL\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RxAtomic = new Regex(
            @"^\s*(?<col>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s*(?<op>>=|<=|<>|=|>|<|LIKE)\s*(?<val>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public FastDataTable() : this(string.Empty) { }

        public FastDataTable(string tableName)
        {
            _tableName = tableName ?? string.Empty;
            _columns = new FastDataColumnCollection(this);
            _rows = new FastDataRowCollection(this);
            _indexes = new Dictionary<int, Dictionary<object, List<FastDataRow>>>();
            _primaryKey = new FastDataColumn[0];

            _expressionEngine = new FastExpressionEngine(this);
        }

        public string TableName
        {
            get { return _tableName; }
            set
            {
                string v = value ?? string.Empty;
                if (DataSet != null &&
                    !string.Equals(_tableName, v, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cannot rename table after adding to DataSet.");

                _tableName = v;
            }
        }

        internal void SetExpressionRecalcFlag(bool value)
        {
            _isRecalculatingExpressions = value;
        }
        public FastDataColumnCollection Columns { get { return _columns; } }
        public FastDataRowCollection Rows { get { return _rows; } }

        public FastDataColumn[] PrimaryKey
        {
            get
            {
                FastDataColumn[] copy = new FastDataColumn[_primaryKey.Length];
                Array.Copy(_primaryKey, copy, _primaryKey.Length);
                return copy;
            }
            set
            {
                if (value == null || value.Length == 0)
                {
                    _primaryKey = new FastDataColumn[0];
                    return;
                }

                FastDataColumn[] copy = new FastDataColumn[value.Length];
                for (int i = 0; i < value.Length; i++)
                {
                    if (value[i] == null || !ReferenceEquals(value[i].Table, this))
                        throw new ArgumentException("PrimaryKey columns must belong to this table.");
                    copy[i] = value[i];
                }

                _primaryKey = copy;
            }
        }

        public int MinimumCapacity
        {
            get { return _minimumCapacity; }
            set
            {
                _minimumCapacity = value < 0 ? 0 : value;
                _rows.EnsureCapacity(_minimumCapacity);
            }
        }

        public DataView DefaultView
        {
            get
            {
                if (_defaultView == null) _defaultView = new DataView(this);
                return _defaultView;
            }
        }

        public FastDataRow NewRow()
        {
            return new FastDataRow(this);
        }

        internal void OnAttachedToDataSet()
        {
            _expressionEngine.MarkDirty();
            if (_rows.InternalList.Count > 0)
                _expressionEngine.RecalculateAllRows();
        }

        internal void AttachRow(FastDataRow row)
        {
            if (row == null) throw new ArgumentNullException("row");
            if (!ReferenceEquals(row.Table, this))
                throw new InvalidOperationException("Row belongs to another table.");
            if (row.RowState != DataRowState.Detached)
                throw new InvalidOperationException("Row is already attached.");

            if (row._values.Length != Columns.Count)
                row.Expand(Columns.Count);

            if (!_isLoading)
                _expressionEngine.RecalculateRow(row, -1);

            _rows.InternalList.Add(row);
            row.RowState = DataRowState.Added;
            OnRowAdded(row);
        }

        internal void OnColumnAdded(FastDataColumn addedColumn)
        {
            int newSize = _columns.Count;
            List<FastDataRow> list = _rows.InternalList;

            for (int i = 0; i < list.Count; i++)
                list[i].Expand(newSize);

            _indexes.Clear();
            _expressionEngine.MarkDirty();

            if (!_isLoading && addedColumn != null && addedColumn.HasExpression && _rows.InternalList.Count > 0)
                _expressionEngine.RecalculateAllRows();

            if (DataSet != null) DataSet.Relations.MarkDirtyForTable(this);
        }

        internal void OnColumnExpressionChanged(FastDataColumn column)
        {
            _expressionEngine.MarkDirty();
            if (!_isLoading && _rows.InternalList.Count > 0)
                _expressionEngine.RecalculateAllRows();
        }

        public void EndLoadData()
        {
            _isLoading = false;

            _expressionEngine.RecalculateAllRows();
            RebuildIndexes();

            if (DataSet != null)
            {
                DataSet.Relations.MarkDirtyForTable(this);
                DataSet.OnTableEndLoadData(this);
            }
        }

        internal void OnRowValueChanged(FastDataRow row, int ordinal, object oldValue, object newValue)
        {
            if (!_isLoading)
            {
                Dictionary<object, List<FastDataRow>> map;
                if (_indexes.TryGetValue(ordinal, out map))
                {
                    RemoveFromIndex(map, oldValue, row);
                    AddToIndex(map, newValue, row);
                }
            }

            if (!_isLoading && !_isRecalculatingExpressions && !Columns[ordinal].HasExpression)
                _expressionEngine.RecalculateRow(row, ordinal);

            if (DataSet != null)
            {
                if (_isLoading) DataSet.Relations.MarkDirtyForTable(this);
                else DataSet.Relations.OnRowValueChanged(row, ordinal, oldValue, newValue);
            }
        }

        internal void OnRelationAdded(FastDataRelation relation)
        {
            if (relation == null || !ReferenceEquals(relation.ParentTable, this)) return;

            _expressionEngine.MarkDirty();
            if (_rows.InternalList.Count > 0)
                _expressionEngine.RecalculateAllRows();
        }

        internal void OnChildTableBulkChanged(FastDataRelation relation)
        {
            if (relation == null || !ReferenceEquals(relation.ParentTable, this)) return;

            FastExpressionEngine.RelationWatch watch;
            if (!_expressionEngine.TryGetRelationWatch(relation.RelationName, out watch)) return;

            if (_rows.InternalList.Count > 0)
                _expressionEngine.RecalculateAllRows();
        }

        internal void OnChildRelationRowAdded(FastDataRelation relation, FastDataRow childRow)
        {
            if (relation == null || childRow == null) return;
            if (!ReferenceEquals(relation.ParentTable, this)) return;

            FastExpressionEngine.RelationWatch watch;
            if (!_expressionEngine.TryGetRelationWatch(relation.RelationName, out watch)) return;

            HashSet<FastDataRow> affected = new HashSet<FastDataRow>();
            AddAffectedParentsByKey(relation, childRow.GetValue(relation.ChildColumn.Ordinal), affected);
            RecalculateAffectedRows(affected);
        }

        internal void OnChildRelationRowDeleted(FastDataRelation relation, FastDataRow childRow)
        {
            if (relation == null || childRow == null) return;
            if (!ReferenceEquals(relation.ParentTable, this)) return;

            FastExpressionEngine.RelationWatch watch;
            if (!_expressionEngine.TryGetRelationWatch(relation.RelationName, out watch)) return;

            HashSet<FastDataRow> affected = new HashSet<FastDataRow>();
            AddAffectedParentsByKey(relation, childRow.GetValue(relation.ChildColumn.Ordinal), affected);
            RecalculateAffectedRows(affected);
        }

        internal void OnChildRelationRowValueChanged(FastDataRelation relation, FastDataRow childRow, int ordinal, object oldValue, object newValue)
        {
            if (relation == null || childRow == null) return;
            if (!ReferenceEquals(relation.ParentTable, this)) return;

            FastExpressionEngine.RelationWatch watch;
            if (!_expressionEngine.TryGetRelationWatch(relation.RelationName, out watch)) return;

            bool fkChanged = ordinal == relation.ChildColumn.Ordinal;
            bool relevant = fkChanged || watch.MatchAnyChildOrdinal || watch.IsChildValueOrdinalRelevant(ordinal);
            if (!relevant) return;

            HashSet<FastDataRow> affected = new HashSet<FastDataRow>();

            if (fkChanged)
            {
                AddAffectedParentsByKey(relation, oldValue, affected);
                AddAffectedParentsByKey(relation, newValue, affected);
            }
            else
            {
                AddAffectedParentsByKey(relation, childRow.GetValue(relation.ChildColumn.Ordinal), affected);
            }

            RecalculateAffectedRows(affected);
        }
        internal void ClearRows()
        {
            // D8: removed rows pass to Detached (System.Data parity): the state is
            // observable on held references and the rows remain re-addable.
            for (int i = 0; i < _rows.InternalList.Count; i++)
                _rows.InternalList[i].RowState = DataRowState.Detached;

            _rows.InternalList.Clear();
            _indexes.Clear();

            if (DataSet != null)
            {
                DataSet.Relations.MarkDirtyForTable(this);
                DataSet.OnTableCleared(this);
            }
        }


        public void BeginLoadData()
        {
            _isLoading = true;
            if (DataSet != null) DataSet.Relations.MarkDirtyForTable(this);
        }


        internal void OnRowAdded(FastDataRow row)
        {
            if (!_isLoading) AddRowToAllBuiltIndexes(row);

            if (DataSet != null)
            {
                if (_isLoading) DataSet.Relations.MarkDirtyForTable(this);
                else DataSet.Relations.OnRowAdded(row);
            }
        }

        internal void OnRowDeleted(FastDataRow row)
        {
            if (!_isLoading) RemoveRowFromAllBuiltIndexes(row);

            if (DataSet != null)
            {
                if (_isLoading) DataSet.Relations.MarkDirtyForTable(this);
                else DataSet.Relations.OnRowDeleted(row);
            }
        }

        internal void OnRowValueChanging(FastDataRow row, int ordinal, object oldValue, object newValue)
        {
            // hook
        }

        internal bool IsLoading { get { return _isLoading; } }

        // D7: physical removal of a just-deleted Added row (state Detached).
        internal void DetachAddedRow(FastDataRow row)
        {
            _rows.InternalList.Remove(row);
        }

        // Existence of a key value in the table (index built on demand).
        internal bool HasKeyValue(int ordinal, object value)
        {
            List<FastDataRow> bucket = FindRowsByOrdinalValue(ordinal, value);
            return bucket != null && bucket.Count > 0;
        }

        // ---------- D12: relation constraints (createConstraints) ----------
        // All checks are PRE-validation (before any state mutation),
        // so that the throw does not leave partially modified data. The checks are
        // disabled during BeginLoadData/EndLoadData (parity with System.Data
        // Fill, which does not apply constraints).

        internal void ValidateChildRowAdd(FastDataRow row)
        {
            if (IsLoading || DataSet == null) return;

            List<FastDataRelation> rels = DataSet.Relations.GetByChildTable(this);
            if (rels == null) return;

            for (int i = 0; i < rels.Count; i++)
            {
                FastDataRelation rel = rels[i];
                if (!rel.CreateConstraints) continue;

                object fk = row.GetValue(rel.ChildColumn.Ordinal);
                if (FastKey.IsNull(fk)) continue; // nullable FK: orphan allowed only if NULL

                if (!rel.ParentTable.HasKeyValue(rel.ParentColumn.Ordinal, fk))
                    throw new InvalidOperationException(
                        "Violazione vincolo: nessun genitore " + rel.ParentTable.TableName + "." +
                        rel.ParentColumn.ColumnName + " per il valore '" + fk +
                        "' (relazione " + rel.RelationName + ").");
            }
        }

        internal void ValidateChildKeyWrite(FastDataRow row, int ordinal, object newValue)
        {
            if (IsLoading || DataSet == null) return;
            if (FastKey.IsNull(newValue)) return;

            List<FastDataRelation> rels = DataSet.Relations.GetByChildTable(this);
            if (rels == null) return;

            for (int i = 0; i < rels.Count; i++)
            {
                FastDataRelation rel = rels[i];
                if (!rel.CreateConstraints) continue;
                if (rel.ChildColumn.Ordinal != ordinal) continue;

                if (!rel.ParentTable.HasKeyValue(rel.ParentColumn.Ordinal, newValue))
                    throw new InvalidOperationException(
                        "Violazione vincolo: nessun genitore " + rel.ParentTable.TableName + "." +
                        rel.ParentColumn.ColumnName + " per il valore '" + newValue +
                        "' (relazione " + rel.RelationName + ").");
            }
        }

        internal void ValidateParentDelete(FastDataRow row)
        {
            if (DataSet == null) return;

            List<FastDataRelation> rels = DataSet.Relations.GetByParentTable(this);
            if (rels == null) return;

            for (int i = 0; i < rels.Count; i++)
            {
                FastDataRelation rel = rels[i];
                if (!rel.CreateConstraints) continue;
                if (rel.GetChildrenList(row).Count > 0)
                    throw new InvalidOperationException(
                        "Violazione vincolo: il genitore ha figli e non puo' essere " +
                        "cancellato (relazione " + rel.RelationName + ").");
            }
        }

        // ---------- P1: System.Data parity ----------

        // ImportRow: copies the values (by column name) of a row, in the Added state.
        public FastDataRow ImportRow(FastDataRow row)
        {
            if (row == null) throw new ArgumentNullException("row");

            FastDataRow nr = NewRow();
            for (int i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].HasExpression) continue;

                string name = _columns[i].ColumnName;
                if (row.Table.Columns.Contains(name))
                    nr[i] = row[row.Table.Columns.GetOrdinal(name)];
            }

            _rows.Add(nr);
            return nr;
        }

        // LoadDataRow: creates and attaches a row with the requested state (Added or Unchanged).
        public FastDataRow LoadDataRow(object[] values, DataRowState state)
        {
            if (values == null) throw new ArgumentNullException("values");
            if (values.Length != _columns.Count)
                throw new ArgumentException("values.Length deve coincidere con il numero di colonne.");
            if (state != DataRowState.Added && state != DataRowState.Unchanged)
                throw new ArgumentException("Stato non supportato per LoadDataRow: " + state);

            FastDataRow nr = NewRow();
            for (int i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].HasExpression) continue;
                nr[i] = values[i];
            }

            _rows.Add(nr);
            if (state == DataRowState.Unchanged) nr.RowState = DataRowState.Unchanged;
            return nr;
        }

        public FastDataRow[] Select()
        {
            return SelectInternal(null, null).ToArray();
        }

        public FastDataRow[] Select(string filterExpression)
        {
            return SelectInternal(filterExpression, null).ToArray();
        }

        public FastDataRow[] Select(string filterExpression, string sort)
        {
            return SelectInternal(filterExpression, sort).ToArray();
        }

        internal List<FastDataRow> SelectInternal(string filterExpression, string sortExpression)
        {
            FilterNode root = ParseFilterTree(filterExpression);

            // Phase E: "col = val" as the only clause -> index bucket for the column
            // (insertion order, identical to the full scan). Same comparator
            // (FastObjectComparer) both in the Dictionary and in EvaluateClause, so
            // the bucket membership coincides with cmp == 0. Excluded cases (full scan):
            // multiple clauses, IS NULL/LIKE/inequalities, ISNULL(col, x) = y
            // (UseIsNullFunction substitutes the value before the comparison) and "col = NULL"
            // (in comparisons with NULL EvaluateClause always returns false).
            // Phase E-bis: on the index path the List is NOT pre-allocated to _rows.Count:
            // at N=100.000 it is an 800 KB (LOH) array on every call even with a single
            // match; the accumulated LOH churn triggered expensive GC2s that masked
            // the index gain (measured: Allocated=782 KB/call in the BDN; probe
            // 2026-10-09: ~6 us/call with a fresh heap but >600 us/call with sustained churn).
            // Capacity = size of the bucket (proportional to the output, like System.Data).
            FilterNode eq = TryGetSingleEqualClause(root);
            if (eq != null && !FastKey.IsNull(eq.Clause.Value))
            {
                // NB: name different from 'result' (CS0136: same-named local in the
                // outer scope of the full scan).
                List<FastDataRow> bucket = FindRowsByOrdinalValue(eq.Clause.Ordinal, eq.Clause.Value);
                List<FastDataRow> eqResult = new List<FastDataRow>(bucket != null ? bucket.Count : 0);
                if (bucket != null)
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        FastDataRow r = bucket[i];
                        if (r.RowState != DataRowState.Deleted) eqResult.Add(r);
                    }
                }
                ApplySort(eqResult, sortExpression);
                return eqResult;
            }

            // Full scan (no filter or filter not eligible for the index): initial
            // capacity limited to 1024. Pre-allocating to _rows.Count created a
            // LOH array of ~8*N bytes (800 KB at N=100.000) on every call even for
            // low selectivity filters — same pathology as Phase E-bis, measured in
            // the range/compound families (probe 2026-10-09). The list grows only as
            // much as needed: the final size stays proportional to the output.
            List<FastDataRow> result = new List<FastDataRow>(Math.Min(_rows.Count, 1024));
            for (int i = 0; i < _rows.InternalList.Count; i++)
            {
                FastDataRow r = _rows.InternalList[i];
                if (r.RowState == DataRowState.Deleted) continue;

                if (root == null || EvaluateFilterNode(r, root))
                    result.Add(r);
            }

            ApplySort(result, sortExpression);
            return result;
        }

        private static FilterNode TryGetSingleEqualClause(FilterNode root)
        {
            if (root == null) return null;
            if (root.Type != FilterNodeType.Clause) return null;
            FilterClause c = root.Clause;
            if (c.Kind != FilterKind.Equal) return null;
            if (c.UseIsNullFunction) return null;
            return root;
        }
        public object Compute(string expression, string filter)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentNullException("expression");

            List<FastDataRow> rows = SelectInternal(filter, null);
            string exp = expression.Trim();
            int p = exp.IndexOf('(');
            int q = exp.LastIndexOf(')');

            // D1: the old fallback `new DataTable().Compute` called this very
            // method (alias DataTable = FastDataTable) -> infinite recursion and
            // StackOverflowException. Without parentheses: NotSupportedException, no crash.
            if (p <= 0 || q <= p)
                throw new NotSupportedException("Compute supporta COUNT/SUM/AVG/MIN/MAX.");

            string fn = exp.Substring(0, p).Trim().ToUpperInvariant();
            string arg = NormalizeColumnToken(exp.Substring(p + 1, q - p - 1).Trim());

            if (fn == "COUNT")
            {
                if (arg == "*" || arg.Length == 0) return rows.Count;
                int ord = Columns.GetOrdinal(arg);
                int c = 0;
                for (int i = 0; i < rows.Count; i++)
                    if (!FastKey.IsNull(rows[i].GetValue(ord))) c++;
                return c;
            }

            // D2: aggregates with type preservation. Contract verified
            // empirically on System.Data (probe 2026-10-09, identical on net48/net8.0):
            //   SUM(int/long/...) -> Int64, SUM(decimal) -> decimal, SUM(double) -> double,
            //   AVG -> type of the column (int -> Int32, long -> Int64 with integer
            //          TRUNCATING division: 11/3 -> 3, 15/4 -> 3, 8000/3 -> 2666; decimal -> decimal;
            //          double -> double),
            //   MIN/MAX -> type of the column (raw value, also strings),
            //   SUM/AVG on a non-numeric column (string/bool/DateTime/object) -> DataException,
            //   empty table / no-match filter / all-NULL column -> COUNT 0 and DBNull.
            if (fn == "SUM" || fn == "AVG" || fn == "MIN" || fn == "MAX")
            {
                int colOrd = Columns.GetOrdinal(arg);
                Type colType = Columns[colOrd].DataType;
                Type baseColType = Nullable.GetUnderlyingType(colType) ?? colType;

                if (fn == "SUM")
                {
                    if (IsIntegralType(baseColType))
                    {
                        // NB: unique names (lsum/lhas) because C# 7.3 does not allow
                        // the same local in sibling/nested blocks within the same method.
                        long lsum = 0L;
                        bool lhas = false;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            object v = rows[i].GetValue(colOrd);
                            if (FastKey.IsNull(v)) continue;
                            lsum += Convert.ToInt64(v, Inv);
                            lhas = true;
                        }
                        return lhas ? (object)lsum : DBNull.Value;
                    }

                    if (baseColType == typeof(decimal))
                    {
                        decimal dsum = 0m;
                        bool dhas = false;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            object v = rows[i].GetValue(colOrd);
                            if (FastKey.IsNull(v)) continue;
                            dsum += Convert.ToDecimal(v, Inv);
                            dhas = true;
                        }
                        return dhas ? (object)dsum : DBNull.Value;
                    }

                    // probe: SUM on double -> Double; float treated as double
                    // (float column not present in test/benchmark schemas).
                    if (baseColType == typeof(double) || baseColType == typeof(float))
                    {
                        double fsum = 0d;
                        bool fhas = false;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            object v = rows[i].GetValue(colOrd);
                            if (FastKey.IsNull(v)) continue;
                            fsum += Convert.ToDouble(v, Inv);
                            fhas = true;
                        }
                        return fhas ? (object)fsum : DBNull.Value;
                    }

                    // probe: SUM(S) -> DataException "Invalid usage of aggregate
                    // function Sum() and Type: String" (same for bool/DateTime/object).
                    throw new System.Data.DataException(
                        "SUM: tipo di colonna non supportato: " + baseColType.Name);
                }

                if (fn == "AVG")
                {
                    // probe: AVG returns the type of the column with integer
                    // truncating division for integers (11/3 -> 3), decimal for decimal,
                    // double for double. NULLs excluded from the count.
                    if (IsIntegralType(baseColType))
                    {
                        long asum = 0L;
                        int an = 0;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            object v = rows[i].GetValue(colOrd);
                            if (FastKey.IsNull(v)) continue;
                            asum += Convert.ToInt64(v, Inv);
                            an++;
                        }
                        if (an == 0) return DBNull.Value;
                        long quot = asum / an;
                        if (baseColType == typeof(long) || baseColType == typeof(ulong))
                            return (object)quot;
                        return Convert.ChangeType(quot, baseColType, Inv);
                    }

                    if (baseColType == typeof(decimal))
                    {
                        decimal adsum = 0m;
                        int dn = 0;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            object v = rows[i].GetValue(colOrd);
                            if (FastKey.IsNull(v)) continue;
                            adsum += Convert.ToDecimal(v, Inv);
                            dn++;
                        }
                        return dn == 0 ? DBNull.Value : (object)(adsum / dn);
                    }

                    if (baseColType == typeof(double) || baseColType == typeof(float))
                    {
                        double xsum = 0d;
                        int xn = 0;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            object v = rows[i].GetValue(colOrd);
                            if (FastKey.IsNull(v)) continue;
                            xsum += Convert.ToDouble(v, Inv);
                            xn++;
                        }
                        return xn == 0 ? DBNull.Value : (object)(xsum / xn);
                    }

                    // probe: AVG(S) -> DataException "Invalid usage of aggregate
                    // function Mean() and Type: String" (same for bool/DateTime/object).
                    throw new System.Data.DataException(
                        "AVG: tipo di colonna non supportato: " + baseColType.Name);
                }

                // MIN / MAX: comparison with the standard comparator (parity with the
                // filters and the sorting), raw value returned -> type of the column.
                bool isMin = fn == "MIN";
                object best = null;
                bool has = false;
                for (int i = 0; i < rows.Count; i++)
                {
                    object v = rows[i].GetValue(colOrd);
                    if (FastKey.IsNull(v)) continue;
                    if (!has) { best = v; has = true; continue; }
                    int cmp = FastObjectComparer.Instance.Compare(v, best);
                    if ((isMin && cmp < 0) || (!isMin && cmp > 0)) best = v;
                }
                return has ? best : DBNull.Value;
            }

            throw new NotSupportedException("Compute supporta COUNT/SUM/AVG/MIN/MAX.");
        }

        private static bool IsIntegralType(Type t)
        {
            return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(sbyte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort);
        }

        public FastDataTable Clone()
        {
            FastDataTable c = new FastDataTable(TableName);

            // Pass 1: base schema  
            for (int i = 0; i < Columns.Count; i++)
            {
                FastDataColumn src = Columns[i];
                c.Columns.Add(src.ColumnName, src.DataType);
            }

            // Pass 2: expression  
            for (int i = 0; i < Columns.Count; i++)
            {
                FastDataColumn src = Columns[i];
                if (!string.IsNullOrWhiteSpace(src.Expression))
                    c.Columns[i].Expression = src.Expression;
            }

            if (_primaryKey.Length > 0)
            {
                FastDataColumn[] pk = new FastDataColumn[_primaryKey.Length];
                for (int i = 0; i < _primaryKey.Length; i++)
                    pk[i] = c.Columns[_primaryKey[i].ColumnName];
                c.PrimaryKey = pk;
            }

            return c;
        }

        // Deep copy: schema (Clone) + rows (Deleted rows are excluded).
        // FastData extension: System.Data has no public equivalent (Clone is schema only).
        public FastDataTable Copy()
        {
            FastDataTable c = Clone();
            c.BeginLoadData();
            try
            {
                for (int i = 0; i < _rows.InternalList.Count; i++)
                {
                    FastDataRow s = _rows.InternalList[i];
                    if (s.RowState == DataRowState.Deleted) continue;

                    FastDataRow d = c.NewRow();
                    int n = Math.Min(s._values.Length, c.Columns.Count);
                    for (int cc = 0; cc < n; cc++)
                    {
                        if (c.Columns[cc].HasExpression) continue;
                        d[cc] = s.GetValue(cc);
                    }
                    c.Rows.Add(d);
                }
            }
            finally
            {
                c.EndLoadData();
            }

            c.AcceptChanges();
            return c;
        }

        public void Merge(FastDataTable source)
        {
            if (source == null) return;

            // schema sync: create missing columns  
            List<KeyValuePair<FastDataColumn, string>> expressionsToApply = new List<KeyValuePair<FastDataColumn, string>>();
            for (int i = 0; i < source.Columns.Count; i++)
            {
                FastDataColumn sc = source.Columns[i];
                if (!Columns.Contains(sc.ColumnName))
                {
                    FastDataColumn nc = Columns.Add(sc.ColumnName, sc.DataType);
                    if (!string.IsNullOrWhiteSpace(sc.Expression))
                        expressionsToApply.Add(new KeyValuePair<FastDataColumn, string>(nc, sc.Expression));
                }
            }

            // apply expressions in second pass  
            for (int i = 0; i < expressionsToApply.Count; i++)
                expressionsToApply[i].Key.Expression = expressionsToApply[i].Value;

            int[] srcToDst = new int[source.Columns.Count];
            for (int i = 0; i < source.Columns.Count; i++)
                srcToDst[i] = Columns.GetOrdinal(source.Columns[i].ColumnName);

            bool usePk = _primaryKey.Length > 0;
            Dictionary<object, FastDataRow> pkIndex = null;
            int[] srcPk = null;
            int[] dstPk = null;

            if (usePk)
            {
                srcPk = new int[_primaryKey.Length];
                dstPk = new int[_primaryKey.Length];

                for (int i = 0; i < _primaryKey.Length; i++)
                {
                    string name = _primaryKey[i].ColumnName;
                    if (!source.Columns.Contains(name))
                        throw new InvalidOperationException("Source table missing primary key column: " + name);

                    srcPk[i] = source.Columns.GetOrdinal(name);
                    dstPk[i] = Columns.GetOrdinal(name);
                }

                pkIndex = BuildPrimaryKeyIndex(dstPk);
            }

            BeginLoadData();
            try
            {
                for (int i = 0; i < source.Rows.InternalList.Count; i++)
                {
                    FastDataRow s = source.Rows.InternalList[i];
                    if (s.RowState == DataRowState.Deleted) continue;

                    FastDataRow d = null;

                    if (usePk)
                    {
                        object k = BuildKey(s, srcPk);
                        pkIndex.TryGetValue(k, out d);
                    }

                    if (d == null)
                    {
                        d = NewRow();
                        for (int c = 0; c < srcToDst.Length; c++)
                        {
                            int dstOrd = srcToDst[c];
                            if (Columns[dstOrd].HasExpression) continue;
                            d[dstOrd] = s[c];
                        }

                        Rows.Add(d);

                        if (usePk)
                            pkIndex[BuildKey(d, dstPk)] = d;
                    }
                    else
                    {
                        for (int c = 0; c < srcToDst.Length; c++)
                        {
                            int dstOrd = srcToDst[c];
                            if (Columns[dstOrd].HasExpression) continue;
                            d[dstOrd] = s[c];
                        }
                    }
                }
            }
            finally
            {
                EndLoadData();
            }
        }

        public IEnumerable<FastDataRow> AsEnumerable()
        {
            return _rows;
        }

        public void AcceptChanges()
        {
            List<FastDataRow> list = _rows.InternalList;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                FastDataRow r = list[i];
                if (r.RowState == DataRowState.Deleted) list.RemoveAt(i);
                else r.RowState = DataRowState.Unchanged;
            }

            RebuildIndexes();
            if (DataSet != null) DataSet.Relations.MarkDirtyForTable(this);
        }

        public IEnumerator<FastDataRow> GetEnumerator()
        {
            return _rows.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private Dictionary<object, List<FastDataRow>> EnsureIndex(int ordinal)
        {
            Dictionary<object, List<FastDataRow>> map;
            if (_indexes.TryGetValue(ordinal, out map)) return map;

            map = new Dictionary<object, List<FastDataRow>>(Math.Max(4, _rows.Count * 2), FastObjectComparer.Instance);
            for (int i = 0; i < _rows.InternalList.Count; i++)
            {
                FastDataRow r = _rows.InternalList[i];
                if (r.RowState == DataRowState.Deleted) continue;
                AddToIndex(map, r.GetValue(ordinal), r);
            }

            _indexes.Add(ordinal, map);
            return map;
        }

        internal List<FastDataRow> FindRowsByOrdinalValue(int ordinal, object value)
        {
            Dictionary<object, List<FastDataRow>> idx = EnsureIndex(ordinal);
            List<FastDataRow> bucket;
            return idx.TryGetValue(FastKey.Normalize(value), out bucket) ? bucket : null;
        }

        internal static void AddToIndex(Dictionary<object, List<FastDataRow>> index, object value, FastDataRow row)
        {
            object key = FastKey.Normalize(value);
            List<FastDataRow> bucket;

            if (!index.TryGetValue(key, out bucket))
            {
                bucket = new List<FastDataRow>(1);
                index.Add(key, bucket);
            }

            bucket.Add(row);
        }

        internal static void RemoveFromIndex(Dictionary<object, List<FastDataRow>> index, object value, FastDataRow row)
        {
            object key = FastKey.Normalize(value);
            List<FastDataRow> bucket;

            if (!index.TryGetValue(key, out bucket)) return;

            int pos = bucket.IndexOf(row);
            if (pos >= 0) bucket.RemoveAt(pos);
            if (bucket.Count == 0) index.Remove(key);
        }

        private void AddRowToAllBuiltIndexes(FastDataRow row)
        {
            foreach (KeyValuePair<int, Dictionary<object, List<FastDataRow>>> kv in _indexes)
                AddToIndex(kv.Value, row.GetValue(kv.Key), row);
        }

        private void RemoveRowFromAllBuiltIndexes(FastDataRow row)
        {
            foreach (KeyValuePair<int, Dictionary<object, List<FastDataRow>>> kv in _indexes)
                RemoveFromIndex(kv.Value, row.GetValue(kv.Key), row);
        }

        private void RebuildIndexes()
        {
            if (_indexes.Count == 0) return;

            int[] ords = new int[_indexes.Count];
            _indexes.Keys.CopyTo(ords, 0);
            _indexes.Clear();

            for (int i = 0; i < ords.Length; i++)
                EnsureIndex(ords[i]);
        }

        private Dictionary<object, FastDataRow> BuildPrimaryKeyIndex(int[] pkOrdinals)
        {
            Dictionary<object, FastDataRow> idx =
                new Dictionary<object, FastDataRow>(Math.Max(4, Rows.Count), FastObjectComparer.Instance);

            for (int i = 0; i < Rows.InternalList.Count; i++)
            {
                FastDataRow r = Rows.InternalList[i];
                if (r.RowState == DataRowState.Deleted) continue;

                object key = BuildKey(r, pkOrdinals);
                if (!idx.ContainsKey(key))
                    idx.Add(key, r);
            }

            return idx;
        }

        private static object BuildKey(FastDataRow row, int[] ordinals)
        {
            if (ordinals.Length == 1)
                return FastKey.Normalize(row.GetValue(ordinals[0]));

            object[] vals = new object[ordinals.Length];
            for (int i = 0; i < ordinals.Length; i++)
                vals[i] = FastKey.Normalize(row.GetValue(ordinals[i]));

            return new CompositeKey(vals);
        }

        private void AddAffectedParentsByKey(FastDataRelation relation, object keyValue, HashSet<FastDataRow> affected)
        {
            List<FastDataRow> parents = FindRowsByOrdinalValue(relation.ParentColumn.Ordinal, keyValue);
            if (parents == null || parents.Count == 0) return;

            for (int i = 0; i < parents.Count; i++)
            {
                FastDataRow p = parents[i];
                if (p.RowState != DataRowState.Deleted)
                    affected.Add(p);
            }
        }

        private void RecalculateAffectedRows(HashSet<FastDataRow> affected)
        {
            if (affected == null || affected.Count == 0) return;

            foreach (FastDataRow p in affected)
            {
                if (p.RowState == DataRowState.Deleted) continue;
                _expressionEngine.RecalculateRow(p, -1);
            }
        }

        private void ApplySort(List<FastDataRow> rows, string sortExpression)
        {
            List<SortClause> clauses = ParseSort(sortExpression);
            if (clauses.Count == 0 || rows.Count <= 1) return;

            rows.Sort(delegate (FastDataRow a, FastDataRow b)
            {
                for (int i = 0; i < clauses.Count; i++)
                {
                    SortClause c = clauses[i];
                    int cmp = FastObjectComparer.Instance.Compare(a.GetValue(c.Ordinal), b.GetValue(c.Ordinal));
                    if (c.Desc) cmp = -cmp;
                    if (cmp != 0) return cmp;
                }
                return 0;
            });
        }

        private List<SortClause> ParseSort(string sortExpression)
        {
            List<SortClause> clauses = new List<SortClause>(2);
            if (string.IsNullOrWhiteSpace(sortExpression)) return clauses;

            string[] parts = sortExpression.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;

                bool desc = false;
                string column = part;

                int sp = part.LastIndexOf(' ');
                if (sp > 0)
                {
                    string dir = part.Substring(sp + 1).Trim();
                    if (string.Equals(dir, "DESC", StringComparison.OrdinalIgnoreCase))
                    {
                        desc = true;
                        column = part.Substring(0, sp).Trim();
                    }
                    else if (string.Equals(dir, "ASC", StringComparison.OrdinalIgnoreCase))
                    {
                        desc = false;
                        column = part.Substring(0, sp).Trim();
                    }
                }

                column = NormalizeColumnToken(column);
                clauses.Add(new SortClause
                {
                    Ordinal = Columns.GetOrdinal(column),
                    Desc = desc
                });
            }

            return clauses;
        }

        private static string NormalizeColumnToken(string token)
        {
            token = token.Trim();
            if (token.Length >= 2 && token[0] == '[' && token[token.Length - 1] == ']')
                token = token.Substring(1, token.Length - 2).Replace("]]", "]");
            return token;
        }

        private FilterNode ParseFilterTree(string filterExpression)
        {
            if (string.IsNullOrWhiteSpace(filterExpression)) return null;
            return ParseFilterTreeInternal(filterExpression.Trim());
        }

        private FilterNode ParseFilterTreeInternal(string expression)
        {
            expression = StripOuterParentheses(expression);

            List<string> orParts = SplitTopLevel(expression, "OR");
            if (orParts.Count > 1)
            {
                FilterNode n = ParseFilterTreeInternal(orParts[0]);
                for (int i = 1; i < orParts.Count; i++)
                {
                    n = new FilterNode
                    {
                        Type = FilterNodeType.Or,
                        Left = n,
                        Right = ParseFilterTreeInternal(orParts[i])
                    };
                }
                return n;
            }

            List<string> andParts = SplitTopLevel(expression, "AND");
            if (andParts.Count > 1)
            {
                FilterNode n = ParseFilterTreeInternal(andParts[0]);
                for (int i = 1; i < andParts.Count; i++)
                {
                    n = new FilterNode
                    {
                        Type = FilterNodeType.And,
                        Left = n,
                        Right = ParseFilterTreeInternal(andParts[i])
                    };
                }
                return n;
            }

            if (StartsWithWord(expression, "NOT"))
            {
                string rest = expression.Substring(3).Trim();
                if (rest.Length == 0) throw new NotSupportedException("Unsupported filter: " + expression);

                return new FilterNode
                {
                    Type = FilterNodeType.Not,
                    Left = ParseFilterTreeInternal(rest)
                };
            }

            FilterClause clause;
            if (!TryParseAtomicClause(expression, out clause))
                throw new NotSupportedException("Unsupported filter: " + expression);

            return new FilterNode
            {
                Type = FilterNodeType.Clause,
                Clause = clause
            };
        }

        private bool EvaluateFilterNode(FastDataRow row, FilterNode node)
        {
            if (node == null) return true;

            switch (node.Type)
            {
                case FilterNodeType.Clause:
                    return EvaluateClause(row, node.Clause);

                case FilterNodeType.And:
                    return EvaluateFilterNode(row, node.Left) && EvaluateFilterNode(row, node.Right);

                case FilterNodeType.Or:
                    return EvaluateFilterNode(row, node.Left) || EvaluateFilterNode(row, node.Right);

                case FilterNodeType.Not:
                    return !EvaluateFilterNode(row, node.Left);

                default:
                    return false;
            }
        }

        private bool EvaluateClause(FastDataRow row, FilterClause c)
        {
            object v = row.GetValue(c.Ordinal);

            //NEW: ISNULL() function support in filter expressions
            if (c.UseIsNullFunction && FastKey.IsNull(v))
                v = c.IsNullFallback;

            if (c.Kind == FilterKind.IsNull) return FastKey.IsNull(v);
            if (c.Kind == FilterKind.IsNotNull) return !FastKey.IsNull(v);

            // DataView behavior: comparisons with NULL => false  
            if (FastKey.IsNull(v) || FastKey.IsNull(c.Value)) return false;

            if (c.Kind == FilterKind.Like)
            {
                string valStr = Convert.ToString(v, Inv);
                string pattern = Convert.ToString(c.Value, Inv);
                return MatchLike(valStr, pattern);
            }

            int cmp = FastObjectComparer.Instance.Compare(v, c.Value);

            switch (c.Kind)
            {
                case FilterKind.Equal: return cmp == 0;
                case FilterKind.NotEqual: return cmp != 0;
                case FilterKind.LessThan: return cmp < 0;
                case FilterKind.LessThanOrEqual: return cmp <= 0;
                case FilterKind.GreaterThan: return cmp > 0;
                case FilterKind.GreaterThanOrEqual: return cmp >= 0;
                default: return false;
            }
        }

        private bool TryParseAtomicClause(string expression, out FilterClause clause)
        {
            clause = new FilterClause();

            // FIRST: try the robust ISNULL(...) parser  
            if (TryParseAtomicIsNullClause(expression, out clause))
                return true;

            Match mNull = RxIsNull.Match(expression);
            if (mNull.Success)
            {
                string col = NormalizeColumnToken(mNull.Groups["col"].Value);
                bool isNot = !string.IsNullOrWhiteSpace(mNull.Groups["not"].Value);

                clause.Ordinal = Columns.GetOrdinal(col);
                clause.Kind = isNot ? FilterKind.IsNotNull : FilterKind.IsNull;
                clause.Value = DBNull.Value;
                return true;
            }

            Match m = RxAtomic.Match(expression);
            if (m.Success)
                return BuildClause(NormalizeColumnToken(m.Groups["col"].Value),
                    m.Groups["op"].Value, m.Groups["val"].Value.Trim(), out clause);

            // D3 (extension): column names with spaces WITHOUT square brackets
            // (e.g. "Con Spazio > 1") and "IS [NOT] NULL" for non-standard names:
            // the regex does not cover them, a manual operator scanner is used.
            return TryParseManualClause(expression, out clause);
        }

        private bool BuildClause(string colName, string op, string rawVal, out FilterClause clause)
        {
            clause = new FilterClause();
            int ord = Columns.GetOrdinal(colName);
            string opU = op.ToUpperInvariant();

            clause.Ordinal = ord;
            clause.IsPattern = false;

            switch (opU)
            {
                case "LIKE":
                    clause.Kind = FilterKind.Like;
                    clause.Value = UnquoteString(rawVal);
                    clause.IsPattern = true;
                    ValidateLikePattern((string)clause.Value);
                    return true;

                case "=":
                    clause.Kind = FilterKind.Equal;
                    break;
                case "<>":
                    clause.Kind = FilterKind.NotEqual;
                    break;
                case "<":
                    clause.Kind = FilterKind.LessThan;
                    break;
                case "<=":
                    clause.Kind = FilterKind.LessThanOrEqual;
                    break;
                case ">":
                    clause.Kind = FilterKind.GreaterThan;
                    break;
                case ">=":
                    clause.Kind = FilterKind.GreaterThanOrEqual;
                    break;
                default:
                    return false;
            }

            clause.Value = ParseLiteral(rawVal, Columns[ord].DataType);
            return true;
        }

        private bool TryParseManualClause(string expression, out FilterClause clause)
        {
            clause = new FilterClause();
            string s = expression.Trim();
            if (s.Length == 0) return false;

            const string IsNotNullSuffix = " IS NOT NULL";
            const string IsNullSuffix = " IS NULL";
            if (s.EndsWith(IsNotNullSuffix, StringComparison.OrdinalIgnoreCase))
                return TryManualNullClause(s.Substring(0, s.Length - IsNotNullSuffix.Length), false, out clause);
            if (s.EndsWith(IsNullSuffix, StringComparison.OrdinalIgnoreCase))
                return TryManualNullClause(s.Substring(0, s.Length - IsNullSuffix.Length), true, out clause);

            int opPos;
            string op;
            int opLen;
            if (!FindTopLevelOperator(s, out opPos, out op, out opLen)) return false;

            string colToken = s.Substring(0, opPos).Trim();
            string valToken = s.Substring(opPos + opLen).Trim();
            if (colToken.Length == 0 || valToken.Length == 0) return false;

            return BuildClause(NormalizeColumnToken(colToken), op, valToken, out clause);
        }

        private bool TryManualNullClause(string colToken, bool isNullKind, out FilterClause clause)
        {
            clause = new FilterClause();
            colToken = colToken.Trim();
            if (colToken.Length == 0) return false;

            clause.Ordinal = Columns.GetOrdinal(NormalizeColumnToken(colToken));
            clause.Kind = isNullKind ? FilterKind.IsNull : FilterKind.IsNotNull;
            clause.Value = DBNull.Value;
            return true;
        }

        // First comparison operator at the top level (outside quotes, square brackets
        // and round parentheses). Square brackets are checked BEFORE quotes: an
        // apostrophe inside [Con'Text] must not toggle the quoting state.
        private static bool FindTopLevelOperator(string s, out int opPos, out string op, out int opLen)
        {
            opPos = -1;
            op = null;
            opLen = 0;

            bool inQuote = false;
            int bracketDepth = 0;
            int parDepth = 0;

            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];

                if (ch == '[') { bracketDepth++; continue; }
                if (ch == ']') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (bracketDepth > 0) continue;

                if (ch == '\'')
                {
                    if (inQuote && i + 1 < s.Length && s[i + 1] == '\'') { i++; continue; }
                    inQuote = !inQuote;
                    continue;
                }
                if (inQuote) continue;

                if (ch == '(') { parDepth++; continue; }
                if (ch == ')') { if (parDepth > 0) parDepth--; continue; }
                if (parDepth > 0) continue;

                if (i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if ((ch == '>' && next == '=') || (ch == '<' && (next == '=' || next == '>')))
                    {
                        opPos = i;
                        op = s.Substring(i, 2);
                        opLen = 2;
                        return true;
                    }
                }

                if (ch == '>' || ch == '<' || ch == '=')
                {
                    opPos = i;
                    op = s.Substring(i, 1);
                    opLen = 1;
                    return true;
                }

                if ((ch == 'L' || ch == 'l') && i + 4 <= s.Length &&
                    string.Compare(s, i, "LIKE", 0, 4, StringComparison.OrdinalIgnoreCase) == 0 &&
                    (i + 4 == s.Length || char.IsWhiteSpace(s[i + 4])))
                {
                    opPos = i;
                    op = "LIKE";
                    opLen = 4;
                    return true;
                }
            }

            return false;
        }

        //NEW 
        private bool TryParseAtomicIsNullClause(string expression, out FilterClause clause)
        {
            clause = new FilterClause();
            if (string.IsNullOrWhiteSpace(expression)) return false;

            string s = expression.Trim();
            int i = 0;
            SkipSpaces(s, ref i);

            if (!StartsWithAt(s, i, "ISNULL")) return false;
            i += 6;
            SkipSpaces(s, ref i);

            if (i >= s.Length || s[i] != '(') return false;

            int close = FindMatchingParen(s, i);
            if (close < 0) return false;

            string args = s.Substring(i + 1, close - i - 1);
            int comma = FindTopLevelComma(args);
            if (comma < 0) return false;

            string colToken = args.Substring(0, comma).Trim();
            string defToken = args.Substring(comma + 1).Trim();

            int p = close + 1;
            SkipSpaces(s, ref p);

            string op;
            if (!TryReadOperator(s, ref p, out op)) return false;

            SkipSpaces(s, ref p);
            if (p >= s.Length) return false;

            string valToken = s.Substring(p).Trim();

            string colName = NormalizeColumnToken(colToken);
            int ord = Columns.GetOrdinal(colName);
            Type colType = Columns[ord].DataType;

            clause.Ordinal = ord;
            clause.UseIsNullFunction = true;
            clause.IsNullFallback = ParseLiteralSafe(defToken, colType);

            if (string.Equals(op, "LIKE", StringComparison.OrdinalIgnoreCase))
            {
                clause.Kind = FilterKind.Like;
                clause.Value = UnquoteString(valToken);
                clause.IsPattern = true;
                ValidateLikePattern((string)clause.Value);
                return true;
            }

            switch (op)
            {
                case "=": clause.Kind = FilterKind.Equal; break;
                case "<>": clause.Kind = FilterKind.NotEqual; break;
                case "<": clause.Kind = FilterKind.LessThan; break;
                case "<=": clause.Kind = FilterKind.LessThanOrEqual; break;
                case ">": clause.Kind = FilterKind.GreaterThan; break;
                case ">=": clause.Kind = FilterKind.GreaterThanOrEqual; break;
                default: return false;
            }

            clause.Value = ParseLiteralSafe(valToken, colType);
            return true;
        }

        private static object ParseLiteralSafe(string token, Type targetType)
        {
            Type t = Nullable.GetUnderlyingType(targetType ?? typeof(string)) ?? (targetType ?? typeof(string));

            if (t == typeof(object))
            {
                // D4: here too, only the unquoted NULL is the keyword.
                string raw = (token ?? string.Empty).Trim();
                bool quoted = raw.Length >= 2 && raw[0] == '\'' && raw[raw.Length - 1] == '\'';
                if (!quoted && string.Equals(raw, "NULL", StringComparison.OrdinalIgnoreCase))
                    return DBNull.Value;
                return UnquoteString(raw);
            }

            return ParseLiteral(token, targetType);
        }

        private static void SkipSpaces(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static bool StartsWithAt(string s, int index, string value)
        {
            if (index + value.Length > s.Length) return false;
            return string.Compare(s, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static int FindMatchingParen(string s, int openIndex)
        {
            int depth = 0;
            bool inQuote = false;

            for (int i = openIndex; i < s.Length; i++)
            {
                char ch = s[i];

                if (ch == '\'')
                {
                    if (inQuote && i + 1 < s.Length && s[i + 1] == '\'') { i++; continue; }
                    inQuote = !inQuote;
                    continue;
                }

                if (inQuote) continue;

                if (ch == '(') depth++;
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                    if (depth < 0) return -1;
                }
            }

            return -1;
        }

        private static int FindTopLevelComma(string s)
        {
            int depth = 0;
            bool inQuote = false;

            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];

                if (ch == '\'')
                {
                    if (inQuote && i + 1 < s.Length && s[i + 1] == '\'') { i++; continue; }
                    inQuote = !inQuote;
                    continue;
                }

                if (inQuote) continue;

                if (ch == '(') depth++;
                else if (ch == ')') { if (depth > 0) depth--; }
                else if (ch == ',' && depth == 0) return i;
            }

            return -1;
        }

        private static bool TryReadOperator(string s, ref int i, out string op)
        {
            op = null;
            if (i >= s.Length) return false;

            if (i + 2 <= s.Length)
            {
                string two = s.Substring(i, 2);
                if (two == ">=" || two == "<=" || two == "<>")
                {
                    op = two;
                    i += 2;
                    return true;
                }
            }

            if (i + 4 <= s.Length && string.Compare(s, i, "LIKE", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                op = "LIKE";
                i += 4;
                return true;
            }

            char c = s[i];
            if (c == '=' || c == '<' || c == '>')
            {
                op = c.ToString();
                i++;
                return true;
            }

            return false;
        }
        //---

        private static List<string> SplitTopLevel(string expression, string word)
        {
            List<string> parts = new List<string>(2);
            int start = 0;
            int parDepth = 0;
            int bracketDepth = 0;
            bool inQuote = false;

            for (int i = 0; i < expression.Length; i++)
            {
                char ch = expression[i];

                if (ch == '\'')
                {
                    if (inQuote && i + 1 < expression.Length && expression[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }
                    inQuote = !inQuote;
                    continue;
                }

                if (inQuote) continue;

                if (ch == '[') { bracketDepth++; continue; }
                if (ch == ']') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (bracketDepth > 0) continue;

                if (ch == '(') { parDepth++; continue; }
                if (ch == ')') { if (parDepth > 0) parDepth--; continue; }

                if (parDepth == 0 && IsWordAt(expression, i, word))
                {
                    parts.Add(expression.Substring(start, i - start).Trim());
                    i += word.Length - 1;
                    start = i + 1;
                }
            }

            parts.Add(expression.Substring(start).Trim());
            return parts;
        }

        private static bool IsWordAt(string text, int index, string word)
        {
            if (index < 0 || index + word.Length > text.Length) return false;
            if (string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            bool leftOk = (index == 0) || IsWordBoundary(text[index - 1]);
            int end = index + word.Length;
            bool rightOk = (end == text.Length) || IsWordBoundary(text[end]);

            return leftOk && rightOk;
        }

        private static bool StartsWithWord(string text, string word)
        {
            if (text.Length < word.Length) return false;
            if (string.Compare(text, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            if (text.Length == word.Length) return true;
            return IsWordBoundary(text[word.Length]);
        }

        private static bool IsWordBoundary(char ch)
        {
            return char.IsWhiteSpace(ch) || ch == '(' || ch == ')' || ch == '[' || ch == ']';
        }

        private static string StripOuterParentheses(string expression)
        {
            string s = expression.Trim();

            while (s.Length >= 2 && s[0] == '(' && s[s.Length - 1] == ')' && HasSingleOuterParentheses(s))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }

            return s;
        }

        private static bool HasSingleOuterParentheses(string text)
        {
            int parDepth = 0;
            int bracketDepth = 0;
            bool inQuote = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (ch == '\'')
                {
                    if (inQuote && i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }
                    inQuote = !inQuote;
                    continue;
                }

                if (inQuote) continue;

                if (ch == '[') { bracketDepth++; continue; }
                if (ch == ']') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (bracketDepth > 0) continue;

                if (ch == '(')
                {
                    parDepth++;
                    continue;
                }

                if (ch == ')')
                {
                    parDepth--;
                    if (parDepth == 0 && i < text.Length - 1) return false;
                    if (parDepth < 0) return false;
                }
            }

            return parDepth == 0;
        }

        // Phase D (D5): LIKE semantics = System.Data contract, verified
        // empirically on net48 and net8.0 (probe 2026-10-09, identical on the two
        // runtimes) — NOT SQL semantics:
        //  - % and * are wildcards (".*") ONLY at the start and/or end of the pattern; in
        //    a middle position they are an error (System.Data.EvaluateException);
        //  - [c] with a SINGLE character between square brackets is equivalent to that
        //    character; any other [...] form (empty, multi-character,
        //    negation, range) is an error;
        //  - all the other characters are literal, including _ ? . + | ^ $ ( ) \;
        //  - case-insensitive and culture-invariant comparison.
        // The compiled regex is cached per pattern (one filter = one pattern).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _likeRegexCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, Regex>();

        private static bool MatchLike(string input, string pattern)
        {
            if (input == null || pattern == null) return false;

            string p = UnquoteString(pattern.Trim());
            Regex rx;
            if (!_likeRegexCache.TryGetValue(p, out rx))
            {
                rx = new Regex(LikeToRegex(p),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
                _likeRegexCache[p] = rx; // race: two equivalent compilations, harmless
            }
            return rx.IsMatch(input);
        }

        // Builds the regex equivalent to System.Data behavior for the
        // given pattern; throws System.Data.EvaluateException on invalid patterns
        // (same exception type as System.Data).
        private static string LikeToRegex(string p)
        {
            int start = 0;
            while (start < p.Length && (p[start] == '%' || p[start] == '*')) start++;
            int end = p.Length;
            while (end > start && (p[end - 1] == '%' || p[end - 1] == '*')) end--;

            StringBuilder sb = new StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                char c = p[i];
                if (c == '%' || c == '*')
                    throw new System.Data.EvaluateException(
                        "Error in Like operator: the string pattern '" + p + "' is invalid.");
                if (c == '[')
                {
                    // validates only the [c] form (exactly one character)
                    if (i + 2 >= p.Length || p[i + 1] == ']' || p[i + 2] != ']')
                        throw new System.Data.EvaluateException(
                            "Error in Like operator: the string pattern '" + p + "' is invalid.");
                    char cls = p[i + 1];
                    if (IsRegexSpecial(cls)) sb.Append('\\');
                    sb.Append(cls);
                    i += 2;
                    continue;
                }
                if (IsRegexSpecial(c)) sb.Append('\\');
                sb.Append(c);
            }

            return (start > 0 ? ".*" : "^") + sb.ToString() + (end < p.Length ? ".*" : "$");
        }

        // Early validation at filter parse time: System.Data throws
        // the EvaluateException at the moment of Select() (expression
        // construction), not during row by row matching; this way even
        // on an empty table the behavior is identical.
        private static void ValidateLikePattern(string pattern)
        {
            if (pattern == null)
                throw new System.Data.EvaluateException(
                    "Error in Like operator: the string pattern is invalid.");
            LikeToRegex(pattern);
        }

        private static bool IsRegexSpecial(char c)
        {
            switch (c)
            {
                case '.': case '\\': case '+': case '*': case '?':
                case '(': case ')': case '[': case ']': case '{': case '}':
                case '|': case '^': case '$':
                    return true;
                default:
                    return false;
            }
        }

        private static string UnquoteString(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            token = token.Trim();

            if (token.Length >= 2 && token[0] == '\'' && token[token.Length - 1] == '\'')
                return token.Substring(1, token.Length - 2).Replace("''", "'");

            return token;
        }

        private static object ParseLiteral(string token, Type targetType)
        {
            token = token.Trim();

            // D4: only the NON-quoted NULL keyword is NULL. The literal 'NULL'
            // (with quotes) is the string value "NULL", as in System.Data.
            bool quoted = token.Length >= 2 && token[0] == '\'' && token[token.Length - 1] == '\'';
            if (!quoted && string.Equals(token, "NULL", StringComparison.OrdinalIgnoreCase))
                return DBNull.Value;

            token = UnquoteString(token);

            if (token.Length >= 2 && token[0] == '#' && token[token.Length - 1] == '#')
                token = token.Substring(1, token.Length - 2);

            Type t = targetType ?? typeof(string);
            t = Nullable.GetUnderlyingType(t) ?? t;

            if (t == typeof(string)) return token;

            if (t == typeof(int))
            {
                int v;
                if (int.TryParse(token, NumberStyles.Any, CultureInfo.CurrentCulture, out v) ||
                    int.TryParse(token, NumberStyles.Any, Inv, out v))
                    return v;
                throw new FormatException("Literal non valido per Int32: " + token);
            }

            if (t == typeof(long))
            {
                long v;
                if (long.TryParse(token, NumberStyles.Any, CultureInfo.CurrentCulture, out v) ||
                    long.TryParse(token, NumberStyles.Any, Inv, out v))
                    return v;
                throw new FormatException("Literal non valido per Int64: " + token);
            }

            if (t == typeof(double))
            {
                double v;
                if (TryParseDoubleFlexible(token, out v))
                    return v;
                throw new FormatException("Literal non valido per Double: " + token);
            }

            if (t == typeof(float))
            {
                double v;
                if (TryParseDoubleFlexible(token, out v))
                    return (float)v;
                throw new FormatException("Literal non valido per Single: " + token);
            }

            if (t == typeof(decimal))
            {
                decimal v;
                if (TryParseDecimalFlexible(token, out v))
                    return v;
                throw new FormatException("Literal non valido per Decimal: " + token);
            }

            if (t == typeof(DateTime))
            {
                DateTime dt;
                if (DateTime.TryParse(token, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt) ||
                    DateTime.TryParse(token, Inv, DateTimeStyles.None, out dt))
                    return dt;
                throw new FormatException("Literal non valido per DateTime: " + token);
            }

            if (t == typeof(bool))
            {
                if (token == "1") return true;
                if (token == "0") return false;
                bool b;
                if (bool.TryParse(token, out b)) return b;
                throw new FormatException("Literal non valido per Boolean: " + token);
            }

            try
            {
                return Convert.ChangeType(token, t, CultureInfo.CurrentCulture);
            }
            catch
            {
                return Convert.ChangeType(token, t, Inv);
            }
        }

        private static bool TryParseDoubleFlexible(string text, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(text)) return false;

            bool hasComma = text.IndexOf(',') >= 0;
            bool hasDot = text.IndexOf('.') >= 0;

            if (hasComma && !hasDot)
            {
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out value)) return true;
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
                return double.TryParse(text, NumberStyles.Any, Inv, out value);
            }

            if (hasDot && !hasComma)
            {
                if (double.TryParse(text, NumberStyles.Any, Inv, out value)) return true;
                return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
            }

            if (double.TryParse(text, NumberStyles.Any, Inv, out value)) return true;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            return double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out value);
        }

        private static bool TryParseDecimalFlexible(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;

            bool hasComma = text.IndexOf(',') >= 0;
            bool hasDot = text.IndexOf('.') >= 0;

            if (hasComma && !hasDot)
            {
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out value)) return true;
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
                return decimal.TryParse(text, NumberStyles.Any, Inv, out value);
            }

            if (hasDot && !hasComma)
            {
                if (decimal.TryParse(text, NumberStyles.Any, Inv, out value)) return true;
                return decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
            }

            if (decimal.TryParse(text, NumberStyles.Any, Inv, out value)) return true;
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out value);
        }

        //private static bool TryAsDouble(object value, out double d)
        //{
        //    if (FastKey.IsNull(value)) { d = 0d; return false; }
        //    if (value is double) { d = (double)value; return true; }
        //    if (value is float) { d = (float)value; return true; }
        //    if (value is decimal) { d = (double)(decimal)value; return true; }
        //    if (value is int) { d = (int)value; return true; }
        //    if (value is long) { d = (long)value; return true; }

        //    return double.TryParse(Convert.ToString(value, Inv), NumberStyles.Any, Inv, out d);
        //}

        private static bool TryAsDouble(object value, out double d)
        {
            return FastNumeric.TryAsDouble(value, out d);
        }

        private static bool TryAsDecimal(object value, out decimal d)
        {
            if (FastKey.IsNull(value)) { d = 0m; return false; }
            if (value is decimal) { d = (decimal)value; return true; }
            if (value is int) { d = (int)value; return true; }
            if (value is long) { d = (long)value; return true; }
            if (value is short) { d = (short)value; return true; }
            if (value is byte) { d = (byte)value; return true; }
            if (value is double) { d = (decimal)(double)value; return true; }
            if (value is float) { d = (decimal)(float)value; return true; }

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
    }

    public class FastDataView : IEnumerable<FastDataRow>
    {
        protected readonly FastDataTable _table;
        private string _rowFilter;
        private string _sort;

        internal FastDataView(FastDataTable table)
        {
            _table = table;
        }

        public string RowFilter
        {
            get { return _rowFilter ?? string.Empty; }
            set { _rowFilter = value; }
        }

        public string Sort
        {
            get { return _sort ?? string.Empty; }
            set { _sort = value; }
        }

        // P1: System.Data parity - Count and indexer on the LIVE view
        // (they reflect filters, sorting and changes to the source table).
        public int Count
        {
            get { return _table.SelectInternal(RowFilter, Sort).Count; }
        }

        public FastDataRow this[int index]
        {
            get
            {
                List<FastDataRow> rows = _table.SelectInternal(RowFilter, Sort);
                if (index < 0 || index >= rows.Count)
                    throw new ArgumentOutOfRangeException("index");
                return rows[index];
            }
        }

        public FastDataTable ToTable()
        {
            FastDataTable copy = _table.Clone();
            List<FastDataRow> rows = _table.SelectInternal(RowFilter, Sort);

            copy.BeginLoadData();
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    FastDataRow nr = copy.NewRow();
                    for (int c = 0; c < copy.Columns.Count; c++)
                    {
                        if (copy.Columns[c].HasExpression) continue;
                        nr[c] = rows[i][c];
                    }
                    copy.Rows.Add(nr);
                }
            }
            finally
            {
                copy.EndLoadData();
            }

            copy.AcceptChanges();
            return copy;
        }

        // System.Data parity: DataView.ToTable(string[] columnNames)
        public FastDataTable ToTable(string[] columnNames)
        {
            if (columnNames == null) throw new ArgumentNullException("columnNames");

            List<FastDataColumn> ordered = new List<FastDataColumn>();
            for (int i = 0; i < columnNames.Length; i++)
            {
                if (string.IsNullOrEmpty(columnNames[i])) continue;
                if (!_table.Columns.Contains(columnNames[i]))
                    throw new ArgumentException("Column not found: " + columnNames[i]);
                ordered.Add(_table.Columns[columnNames[i]]);
            }

            FastDataTable copy = new FastDataTable(_table.TableName);
            for (int i = 0; i < ordered.Count; i++)
                copy.Columns.Add(ordered[i].ColumnName, ordered[i].DataType);

            List<FastDataRow> rows = _table.SelectInternal(RowFilter, Sort);
            copy.BeginLoadData();
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    FastDataRow nr = copy.NewRow();
                    for (int c = 0; c < ordered.Count; c++)
                        nr[c] = rows[i][ordered[c].Ordinal];
                    copy.Rows.Add(nr);
                }
            }
            finally
            {
                copy.EndLoadData();
            }

            copy.AcceptChanges();
            return copy;
        }

        public IEnumerator<FastDataRow> GetEnumerator()
        {
            List<FastDataRow> rows = _table.SelectInternal(RowFilter, Sort);
            return rows.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    // Name compatible with legacy code  
    public sealed class DataView : FastDataView
    {
        internal DataView(FastDataTable table) : base(table) { }
    }

    public sealed class FastDataTableCollection : IEnumerable<FastDataTable>
    {
        private readonly FastDataSet _dataSet;
        private readonly List<FastDataTable> _list;
        private readonly Dictionary<string, FastDataTable> _byName;

        internal FastDataTableCollection(FastDataSet dataSet)
        {
            _dataSet = dataSet;
            _list = new List<FastDataTable>(8);
            _byName = new Dictionary<string, FastDataTable>(StringComparer.OrdinalIgnoreCase);
        }

        public int Count { get { return _list.Count; } }

        public FastDataTable this[string tableName]
        {
            get
            {
                FastDataTable t;
                return _byName.TryGetValue(tableName, out t) ? t : null;
            }
        }

        public void Add(FastDataTable table)
        {
            if (table == null) throw new ArgumentNullException("table");

            if (string.IsNullOrWhiteSpace(table.TableName))
                table.TableName = "Table" + (_list.Count + 1).ToString(CultureInfo.InvariantCulture);

            if (_byName.ContainsKey(table.TableName))
                throw new ArgumentException("Table already exists: " + table.TableName);

            if (table.DataSet != null && !ReferenceEquals(table.DataSet, _dataSet))
                throw new InvalidOperationException("Table belongs to another DataSet.");

            table.DataSet = _dataSet;
            _list.Add(table);
            _byName.Add(table.TableName, table);

            _dataSet.Relations.MarkDirtyForTable(table);
            table.OnAttachedToDataSet();
        }

        public bool Contains(string tableName)
        {
            return _byName.ContainsKey(tableName);
        }

        public bool Remove(string tableName)
        {
            FastDataTable t;
            if (!_byName.TryGetValue(tableName, out t))
                return false;

            _byName.Remove(tableName);
            _list.Remove(t);
            t.DataSet = null;
            return true;
        }

        public IEnumerator<FastDataTable> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public sealed class FastDataRelation
    {
        private Dictionary<object, List<FastDataRow>> _childrenByParentKey;
        private static readonly FastDataRow[] EmptyRows = new FastDataRow[0];
        private static readonly List<FastDataRow> EmptyList = new List<FastDataRow>(0);

        private bool _dirty = true;


        public string RelationName { get; private set; }
        public FastDataColumn ParentColumn { get; private set; }
        public FastDataColumn ChildColumn { get; private set; }

        // D12: createConstraints (the parameter was previously ignored).
        public bool CreateConstraints { get; private set; }

        public FastDataTable ParentTable { get { return ParentColumn.Table; } }
        public FastDataTable ChildTable { get { return ChildColumn.Table; } }

        public FastDataRelation(string relationName, FastDataColumn parentColumn, FastDataColumn childColumn)
            : this(relationName, parentColumn, childColumn, true)
        {
        }

        public FastDataRelation(string relationName, FastDataColumn parentColumn, FastDataColumn childColumn, bool createConstraints)
        {
            if (parentColumn == null) throw new ArgumentNullException("parentColumn");
            if (childColumn == null) throw new ArgumentNullException("childColumn");

            RelationName = string.IsNullOrWhiteSpace(relationName)
                ? "Relation_" + Guid.NewGuid().ToString("N")
                : relationName;

            ParentColumn = parentColumn;
            ChildColumn = childColumn;
            CreateConstraints = createConstraints;
        }

        internal void MarkDirty()
        {
            _dirty = true;
        }

        internal List<FastDataRow> GetChildrenList(FastDataRow parentRow)
        {
            if (!ReferenceEquals(parentRow.Table, ParentTable))
                return EmptyList;

            EnsureIndex();

            object parentKey = FastKey.Normalize(parentRow.GetValue(ParentColumn.Ordinal));
            List<FastDataRow> bucket;

            return _childrenByParentKey.TryGetValue(parentKey, out bucket) ? bucket : EmptyList;
        }

        internal FastDataRow[] GetChildren(FastDataRow parentRow)
        {
            List<FastDataRow> list = GetChildrenList(parentRow);
            if (list.Count == 0) return EmptyRows;

            FastDataRow[] res = new FastDataRow[list.Count];
            list.CopyTo(res);
            return res;
        }

        private void EnsureIndex()
        {
            if (!_dirty && _childrenByParentKey != null) return;

            _childrenByParentKey = new Dictionary<object, List<FastDataRow>>(
                Math.Max(4, ChildTable.Rows.Count * 2),
                FastObjectComparer.Instance);

            int childOrd = ChildColumn.Ordinal;
            List<FastDataRow> rows = ChildTable.Rows.InternalList;

            for (int i = 0; i < rows.Count; i++)
            {
                FastDataRow r = rows[i];
                if (r.RowState == DataRowState.Deleted) continue;
                AddToMap(_childrenByParentKey, r.GetValue(childOrd), r);
            }

            _dirty = false;
        }


        internal void OnChildRowAdded(FastDataRow row)
        {
            if (_dirty || !ReferenceEquals(row.Table, ChildTable)) return;
            if (row.RowState == DataRowState.Deleted) return;

            AddToMap(_childrenByParentKey, row.GetValue(ChildColumn.Ordinal), row);
        }

        internal void OnChildRowDeleted(FastDataRow row)
        {
            if (_dirty || !ReferenceEquals(row.Table, ChildTable)) return;
            RemoveFromMap(_childrenByParentKey, row.GetValue(ChildColumn.Ordinal), row);
        }

        internal void OnChildRowValueChanged(FastDataRow row, int ordinal, object oldValue, object newValue)
        {
            if (_dirty || !ReferenceEquals(row.Table, ChildTable)) return;
            if (ordinal != ChildColumn.Ordinal) return;

            RemoveFromMap(_childrenByParentKey, oldValue, row);
            AddToMap(_childrenByParentKey, newValue, row);
        }

        private static void AddToMap(Dictionary<object, List<FastDataRow>> map, object value, FastDataRow row)
        {
            object key = FastKey.Normalize(value);
            List<FastDataRow> list;

            if (!map.TryGetValue(key, out list))
            {
                list = new List<FastDataRow>(1);
                map.Add(key, list);
            }

            list.Add(row);
        }

        private static void RemoveFromMap(Dictionary<object, List<FastDataRow>> map, object value, FastDataRow row)
        {
            object key = FastKey.Normalize(value);
            List<FastDataRow> list;

            if (!map.TryGetValue(key, out list)) return;

            int pos = list.IndexOf(row);
            if (pos >= 0) list.RemoveAt(pos);
            if (list.Count == 0) map.Remove(key);
        }
    }


    public sealed class FastDataRelationCollection : IEnumerable<FastDataRelation>
    {
        private readonly FastDataSet _dataSet;
        private readonly List<FastDataRelation> _list;
        private readonly Dictionary<string, FastDataRelation> _byName;
        private readonly Dictionary<FastDataTable, List<FastDataRelation>> _byChildTable;
        // D12: parent-side mirror, for the deletion constraint validation.
        private readonly Dictionary<FastDataTable, List<FastDataRelation>> _byParentTable;

        internal FastDataRelationCollection(FastDataSet dataSet)
        {
            _dataSet = dataSet;
            _list = new List<FastDataRelation>(8);
            _byName = new Dictionary<string, FastDataRelation>(StringComparer.OrdinalIgnoreCase);
            _byChildTable = new Dictionary<FastDataTable, List<FastDataRelation>>();
            _byParentTable = new Dictionary<FastDataTable, List<FastDataRelation>>();
        }

        public int Count { get { return _list.Count; } }

        public FastDataRelation this[string relationName]
        {
            get
            {
                FastDataRelation rel;
                return _byName.TryGetValue(relationName, out rel) ? rel : null;
            }
        }

        public FastDataRelation Add(string relationName, FastDataColumn parentColumn, FastDataColumn childColumn)
        {
            return Add(relationName, parentColumn, childColumn, true);
        }

        public FastDataRelation Add(string relationName, FastDataColumn parentColumn, FastDataColumn childColumn, bool createConstraints)
        {
            // D12: the createConstraints flag is now propagated to the relation.
            FastDataRelation rel = new FastDataRelation(relationName, parentColumn, childColumn, createConstraints);
            Add(rel);
            return rel;
        }

        public void Add(FastDataRelation relation)
        {
            if (relation == null) throw new ArgumentNullException("relation");
            if (relation.ParentTable == null || relation.ChildTable == null)
                throw new InvalidOperationException("Columns must belong to tables.");
            if (!ReferenceEquals(relation.ParentTable.DataSet, _dataSet) ||
                !ReferenceEquals(relation.ChildTable.DataSet, _dataSet))
                throw new InvalidOperationException("Both tables must belong to this DataSet.");
            if (_byName.ContainsKey(relation.RelationName))
                throw new ArgumentException("Relation already exists: " + relation.RelationName);

            _list.Add(relation);
            _byName.Add(relation.RelationName, relation);

            List<FastDataRelation> childList;
            if (!_byChildTable.TryGetValue(relation.ChildTable, out childList))
            {
                childList = new List<FastDataRelation>(2);
                _byChildTable.Add(relation.ChildTable, childList);
            }

            childList.Add(relation);

            // D12: parent-side mirror (deletion constraint).
            List<FastDataRelation> parentList;
            if (!_byParentTable.TryGetValue(relation.ParentTable, out parentList))
            {
                parentList = new List<FastDataRelation>(2);
                _byParentTable.Add(relation.ParentTable, parentList);
            }

            parentList.Add(relation);
            relation.MarkDirty();

            _dataSet.OnRelationAdded(relation);
        }

        internal FastDataRow[] GetChildren(string relationName, FastDataRow parentRow)
        {
            FastDataRelation rel;
            if (!_byName.TryGetValue(relationName, out rel))
                return new FastDataRow[0];

            return rel.GetChildren(parentRow);
        }

        internal List<FastDataRelation> GetByChildTable(FastDataTable table)
        {
            List<FastDataRelation> rels;
            if (!_byChildTable.TryGetValue(table, out rels) || rels.Count == 0)
                return null;

            return new List<FastDataRelation>(rels);
        }

        // D12: relations in which the table is the PARENT (deletion constraint).
        internal List<FastDataRelation> GetByParentTable(FastDataTable table)
        {
            List<FastDataRelation> rels;
            if (!_byParentTable.TryGetValue(table, out rels) || rels.Count == 0)
                return null;

            return new List<FastDataRelation>(rels);
        }

        internal void OnRowAdded(FastDataRow row)
        {
            List<FastDataRelation> rels;
            if (!_byChildTable.TryGetValue(row.Table, out rels)) return;

            for (int i = 0; i < rels.Count; i++)
            {
                FastDataRelation rel = rels[i];
                rel.OnChildRowAdded(row);
                _dataSet.OnChildRowAdded(rel, row);
            }
        }

        internal void OnRowDeleted(FastDataRow row)
        {
            List<FastDataRelation> rels;
            if (!_byChildTable.TryGetValue(row.Table, out rels)) return;

            for (int i = 0; i < rels.Count; i++)
            {
                FastDataRelation rel = rels[i];
                rel.OnChildRowDeleted(row);
                _dataSet.OnChildRowDeleted(rel, row);
            }
        }

        internal void OnRowValueChanged(FastDataRow row, int ordinal, object oldValue, object newValue)
        {
            List<FastDataRelation> rels;
            if (!_byChildTable.TryGetValue(row.Table, out rels)) return;

            for (int i = 0; i < rels.Count; i++)
            {
                FastDataRelation rel = rels[i];
                rel.OnChildRowValueChanged(row, ordinal, oldValue, newValue);
                _dataSet.OnChildRowValueChanged(rel, row, ordinal, oldValue, newValue);
            }
        }

        internal void MarkDirtyForTable(FastDataTable table)
        {
            List<FastDataRelation> rels;
            if (!_byChildTable.TryGetValue(table, out rels)) return;

            for (int i = 0; i < rels.Count; i++)
                rels[i].MarkDirty();
        }

        public IEnumerator<FastDataRelation> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public sealed class FastDataSet
    {
        public FastDataSet()
        {
            Tables = new FastDataTableCollection(this);
            Relations = new FastDataRelationCollection(this);
        }

        public FastDataTableCollection Tables { get; private set; }
        public FastDataRelationCollection Relations { get; private set; }

        internal void OnRelationAdded(FastDataRelation relation)
        {
            if (relation == null) return;
            relation.ParentTable.OnRelationAdded(relation);
        }

        internal void OnChildRowAdded(FastDataRelation relation, FastDataRow childRow)
        {
            if (relation == null || childRow == null) return;
            relation.ParentTable.OnChildRelationRowAdded(relation, childRow);
        }

        internal void OnChildRowDeleted(FastDataRelation relation, FastDataRow childRow)
        {
            if (relation == null || childRow == null) return;
            relation.ParentTable.OnChildRelationRowDeleted(relation, childRow);
        }

        internal void OnChildRowValueChanged(FastDataRelation relation, FastDataRow childRow, int ordinal, object oldValue, object newValue)
        {
            if (relation == null || childRow == null) return;
            relation.ParentTable.OnChildRelationRowValueChanged(relation, childRow, ordinal, oldValue, newValue);
        }

        internal void OnTableEndLoadData(FastDataTable table)
        {
            if (table == null) return;

            List<FastDataRelation> rels = Relations.GetByChildTable(table);
            if (rels == null) return;

            for (int i = 0; i < rels.Count; i++)
                rels[i].ParentTable.OnChildTableBulkChanged(rels[i]);
        }

        internal void OnTableCleared(FastDataTable table)
        {
            OnTableEndLoadData(table);
        }

        public void AcceptChanges()
        {
            foreach (FastDataTable t in Tables)
                t.AcceptChanges();
        }
    }

    public static class FastDataExtensions
    {
        public static FastDataTable CopyToDataTable(this IEnumerable<FastDataRow> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            List<FastDataRow> rows = source.ToList();
            if (rows.Count == 0) throw new InvalidOperationException("Nessuna riga da copiare.");

            FastDataTable result = rows[0].Table.Clone();

            for (int r = 0; r < rows.Count; r++)
            {
                FastDataRow nr = result.NewRow();
                for (int i = 0; i < result.Columns.Count; i++)
                {
                    if (result.Columns[i].HasExpression) continue;
                    nr[i] = rows[r][i];
                }
                result.Rows.Add(nr);
            }

            return result;
        }

        public static IEnumerable<FastDataRow> AsEnumerable(this FastDataTable table)
        {
            if (table == null) throw new ArgumentNullException("table");
            return table.Rows;
        }

        public static T Field<T>(this FastDataRow row, string columnName)
        {
            if (row == null) throw new ArgumentNullException("row");
            return row.Field<T>(columnName);
        }

        public static T Field<T>(this FastDataRow row, int ordinal)
        {
            if (row == null) throw new ArgumentNullException("row");
            return row.Field<T>(ordinal);
        }
    }
}
