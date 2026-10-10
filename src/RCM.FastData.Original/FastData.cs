using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DataColumn = RCM.FastData.Original.FastDataColumn;
using DataRow = RCM.FastData.Original.FastDataRow;
using DataSet = RCM.FastData.Original.FastDataSet;
using DataTable = RCM.FastData.Original.FastDataTable;
using DataView = RCM.FastData.Original.FastDataView;

namespace RCM.FastData.Original
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

            // Aligned with literal parsing: comma -> try it/current/invariant  
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

            // mixed or without separators  
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
            object oldValue = _values[ordinal];

            if (FastObjectComparer.Instance.Equals(FastKey.Normalize(oldValue), FastKey.Normalize(newValue)))
                return;

            if (RowState != DataRowState.Detached)
                Table.OnRowValueChanging(this, ordinal, oldValue, newValue);

            _values[ordinal] = newValue;

            if (RowState != DataRowState.Detached)
                Table.OnRowValueChanged(this, ordinal, oldValue, newValue);

            if (!isCalculatedWrite && RowState == DataRowState.Unchanged)
                RowState = DataRowState.Modified;
        }

        public void Delete()
        {
            if (RowState == DataRowState.Detached)
                throw new InvalidOperationException("Row is detached.");
            if (RowState == DataRowState.Deleted) return;

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
            if (FastKey.IsNull(value)) return default(T);
            if (value is T) return (T)value;
            return (T)Convert.ChangeType(value, typeof(T), Inv);
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

        // Expression engine - compiled delegates
        private Func<FastDataRow, object>[] _compiledExpressions = new Func<FastDataRow, object>[0];
        private int[] _expressionOrdinals = new int[0];
        private ChildAggregatePlan[] _childAggPlans = new ChildAggregatePlan[0];
        private Dictionary<string, List<ChildAggregatePlan>> _childAggByRelation =
            new Dictionary<string, List<ChildAggregatePlan>>(StringComparer.OrdinalIgnoreCase);
        //private bool _expressionsCompiled = false;
        //private bool _isRecalculatingExpressions;
        private bool _exprSchemaDirty = true;
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

        //private static readonly Regex RxIsNull = new Regex(
        //    @"^\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s+IS\s+(?<not>NOT\s+)?NULL\s*$",
        //    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        //private static readonly Regex RxAtomic = new Regex(
        //    @"^\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s*(?<op>>=|<=|<>|=|>|<|LIKE)\s*(?<val>.+?)\s*$",
        //    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        //private static readonly Regex RxAtomicIsNullFunc = new Regex(
        //    @"^\s*ISNULL\s*$\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s*,\s*(?<def>.+?)\s*$\s*(?<op>>=|<=|<>|=|>|<|LIKE)\s*(?<val>.+?)\s*$",
        //    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RxIsNull = new Regex(
            @"^\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s+IS\s+(?<not>NOT\s+)?NULL\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RxAtomic = new Regex(
            @"^\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s*(?<op>>=|<=|<>|=|>|<|LIKE)\s*(?<val>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        //private static readonly Regex RxIsNull = new Regex(
        //    @"^\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s+IS\s+(?<not>NOT\s+)?NULL\s*$",
        //    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        //private static readonly Regex RxAtomic = new Regex(
        //    @"^\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s*(?<op>>=|<=|<>|=|>|<|LIKE)\s*(?<val>.+?)\s*$",
        //    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RxAtomicIsNullFunc = new Regex(
            @"^\s*ISNULL\s*$\s*(?<col>$[^$]+\]|[A-Za-z_][A-Za-z0-9_\.]*)\s*,\s*(?<def>.*?)\s*$\s*(?<op>>=|<=|<>|=|>|<|LIKE)\s*(?<val>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private enum AggregateFunction
        {
            Sum,
            Avg,
            Min,
            Max,
            Count
        }

        private sealed class ExpressionCompilePlan
        {
            public int Ordinal;
            public string OriginalExpression;
            public string RewrittenExpression;
        }

        private sealed class ChildAggregatePlan
        {
            public int Ordinal; // owner expression column  
            public AggregateFunction Function;
            public string RelationName;
            public string ChildColumnName;
            public string PlaceholderColumnName;
            public Type PlaceholderType;
        }

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
            List<FastDataRow> result = new List<FastDataRow>(_rows.Count);

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
        public object Compute(string expression, string filter)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentNullException("expression");

            List<FastDataRow> rows = SelectInternal(filter, null);
            string exp = expression.Trim();
            int p = exp.IndexOf('(');
            int q = exp.LastIndexOf(')');
            if (p <= 0 || q <= p)
            {
                try
                {
                    return new DataTable().Compute("(" + exp + ")", null);
                }
                catch { }
                throw new NotSupportedException("Compute supporta COUNT/SUM/AVG/MIN/MAX.");
            }

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

            int colOrd = Columns.GetOrdinal(arg);
            bool has = false;
            double sum = 0d, min = 0d, max = 0d;
            int n = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                double d;
                if (!TryAsDouble(rows[i].GetValue(colOrd), out d)) continue;

                if (!has)
                {
                    has = true;
                    min = d;
                    max = d;
                }
                else
                {
                    if (d < min) min = d;
                    if (d > max) max = d;
                }

                sum += d;
                n++;
            }

            switch (fn)
            {
                case "SUM": return has ? (object)sum : DBNull.Value;
                case "AVG": return n == 0 ? (object)DBNull.Value : (object)(sum / n);
                case "MIN": return has ? (object)min : DBNull.Value;
                case "MAX": return has ? (object)max : DBNull.Value;
                default:
                    throw new NotSupportedException("Compute supporta COUNT/SUM/AVG/MIN/MAX.");
            }
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

            //NEW: support for the ISNULL() function in filter expressions
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
            if (!m.Success) return false;

            string colName = NormalizeColumnToken(m.Groups["col"].Value);
            int ord = Columns.GetOrdinal(colName);
            string op = m.Groups["op"].Value.ToUpperInvariant();
            string rawVal = m.Groups["val"].Value.Trim();

            clause.Ordinal = ord;
            clause.IsPattern = false;

            switch (op)
            {
                case "LIKE":
                    clause.Kind = FilterKind.Like;
                    clause.Value = UnquoteString(rawVal);
                    clause.IsPattern = true;
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
                token = UnquoteString((token ?? string.Empty).Trim());
                if (string.Equals(token, "NULL", StringComparison.OrdinalIgnoreCase))
                    return DBNull.Value;
                return token;
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

        private static bool MatchLike(string input, string pattern)
        {
            if (input == null || pattern == null) return false;

            string p = UnquoteString(pattern.Trim());
            string regexPattern = "^" + RegexEscape(p) + "$";

            return Regex.IsMatch(
                input,
                regexPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }

        private static string RegexEscape(string pattern)
        {
            StringBuilder result = new StringBuilder(pattern.Length * 2);

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '%':
                    case '*':
                        result.Append(".*");
                        break;

                    case '_':
                    case '?':
                        result.Append(".");
                        break;

                    case '.':
                    case '+':
                    case '{':
                    case '}':
                    case '[':
                    case ']':
                    case '(':
                    case ')':
                    case '|':
                    case '^':
                    case '$':
                    case '\\':
                        result.Append('\\').Append(c);
                        break;

                    default:
                        result.Append(c);
                        break;
                }
            }

            return result.ToString();
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
            token = UnquoteString(token);

            if (token.Length >= 2 && token[0] == '#' && token[token.Length - 1] == '#')
                token = token.Substring(1, token.Length - 2);

            if (string.Equals(token, "NULL", StringComparison.OrdinalIgnoreCase))
                return DBNull.Value;

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

        public FastDataTable ParentTable { get { return ParentColumn.Table; } }
        public FastDataTable ChildTable { get { return ChildColumn.Table; } }

        public FastDataRelation(string relationName, FastDataColumn parentColumn, FastDataColumn childColumn)
        {
            if (parentColumn == null) throw new ArgumentNullException("parentColumn");
            if (childColumn == null) throw new ArgumentNullException("childColumn");

            RelationName = string.IsNullOrWhiteSpace(relationName)
                ? "Relation_" + Guid.NewGuid().ToString("N")
                : relationName;

            ParentColumn = parentColumn;
            ChildColumn = childColumn;
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


    internal static class FastExpressionCompiler
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<FastDataRow, object>> _cache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Func<FastDataRow, object>>(System.StringComparer.Ordinal);

        internal static Func<FastDataRow, object> Compile(FastDataTable table, string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            string key = table.TableName + "|" + expression;

            return _cache.GetOrAdd(key, k =>
            {
                var expr = expression.Trim();
                return CompileExpression(table, expr);
            });
        }

        private static Func<FastDataRow, object> CompileExpression(FastDataTable table, string expression)
        {
            var param = System.Linq.Expressions.Expression.Parameter(typeof(FastDataRow), "row");
            var body = BuildExpressionTree(table, expression, param);

            return System.Linq.Expressions.Expression.Lambda<Func<FastDataRow, object>>(
                System.Linq.Expressions.Expression.Convert(body, typeof(object)), param
            ).Compile();
        }

        private static System.Linq.Expressions.Expression BuildExpressionTree(FastDataTable table, string expression, System.Linq.Expressions.ParameterExpression rowParam)
        {
            int pos = 0;
            var result = ParseExpression(table, expression, ref pos, rowParam);
            return result;
        }

        private static System.Linq.Expressions.Expression ParseExpression(FastDataTable table, string expression, ref int pos, System.Linq.Expressions.ParameterExpression rowParam)
        {
            var left = ParseTerm(table, expression, ref pos, rowParam);

            while (pos < expression.Length)
            {
                SkipWhitespace(expression, ref pos);
                if (pos >= expression.Length) break;

                char op = expression[pos];
                if (op != '+' && op != '-') break;
                pos++;

                SkipWhitespace(expression, ref pos);
                var right = ParseTerm(table, expression, ref pos, rowParam);

                left = op == '+'
                    ? System.Linq.Expressions.Expression.Add(left, right)
                    : System.Linq.Expressions.Expression.Subtract(left, right);
            }

            return left;
        }

        private static System.Linq.Expressions.Expression ParseTerm(FastDataTable table, string expression, ref int pos, System.Linq.Expressions.ParameterExpression rowParam)
        {
            var left = ParseFactor(table, expression, ref pos, rowParam);

            while (pos < expression.Length)
            {
                SkipWhitespace(expression, ref pos);
                if (pos >= expression.Length) break;

                char op = expression[pos];
                if (op != '*' && op != '/') break;
                pos++;

                SkipWhitespace(expression, ref pos);
                var right = ParseFactor(table, expression, ref pos, rowParam);

                left = op == '*'
                    ? System.Linq.Expressions.Expression.Multiply(left, right)
                    : System.Linq.Expressions.Expression.Divide(left, right);
            }

            return left;
        }

        private static System.Linq.Expressions.Expression ParseFactor(FastDataTable table, string expression, ref int pos, System.Linq.Expressions.ParameterExpression rowParam)
        {
            SkipWhitespace(expression, ref pos);
            if (pos >= expression.Length) return System.Linq.Expressions.Expression.Constant(System.DBNull.Value);

            char ch = expression[pos];

            if (ch == '(')
            {
                pos++;
                var result = ParseExpression(table, expression, ref pos, rowParam);
                SkipWhitespace(expression, ref pos);
                if (pos < expression.Length && expression[pos] == ')')
                    pos++;
                return result;
            }

            if (ch == '[')
            {
                return ParseColumnReference(table, expression, ref pos, rowParam);
            }

            if (char.IsDigit(ch) || ch == '-' || ch == '.')
            {
                return ParseLiteral(table, expression, ref pos);
            }

            return System.Linq.Expressions.Expression.Constant(System.DBNull.Value);
        }

        private static System.Linq.Expressions.Expression ParseColumnReference(FastDataTable table, string expression, ref int pos, System.Linq.Expressions.ParameterExpression rowParam)
        {
            pos++; // skip '['
            int start = pos;
            while (pos < expression.Length && expression[pos] != ']') pos++;

            string columnName = expression.Substring(start, pos - start).Trim();
            pos++; // skip ']'

            if (table.Columns.Contains(columnName))
            {
                int ordinal = table.Columns.GetOrdinal(columnName);
                var getMethod = typeof(FastDataRow).GetProperty("Item").GetGetMethod();
                return System.Linq.Expressions.Expression.Convert(
                    System.Linq.Expressions.Expression.Call(rowParam, getMethod, System.Linq.Expressions.Expression.Constant(ordinal)),
                    typeof(object)
                );
            }

            return System.Linq.Expressions.Expression.Constant(System.DBNull.Value);
        }

        private static System.Linq.Expressions.Expression ParseLiteral(FastDataTable table, string expression, ref int pos)
        {
            int start = pos;
            bool hasMinus = false;

            if (expression[pos] == '-')
            {
                hasMinus = true;
                pos++;
            }

            while (pos < expression.Length && char.IsWhiteSpace(expression[pos])) pos++;

            if (pos < expression.Length && expression[pos] == '\'')
            {
                int quoteEnd = pos + 1;
                while (quoteEnd < expression.Length && expression[quoteEnd] != '\'') quoteEnd++;


                string literal = expression.Substring(pos + 1, quoteEnd - pos - 1).Replace("''", "'");
                pos = quoteEnd + 1;
                return System.Linq.Expressions.Expression.Constant(literal);
            }

            while (pos < expression.Length &&
                   (char.IsDigit(expression[pos]) || expression[pos] == '.' ||
                    expression[pos] == 'e' || expression[pos] == 'E' ||
                    expression[pos] == '+' || expression[pos] == '-'))
            {
                pos++;
            }

            string numStr = expression.Substring(start, pos - start).Trim();

            if (hasMinus) numStr = "-" + numStr;

            double d;
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d))
            {
                return System.Linq.Expressions.Expression.Constant(d);
            }

            return System.Linq.Expressions.Expression.Constant(System.DBNull.Value);
        }

        private static void SkipWhitespace(string expression, ref int pos)
        {
            while (pos < expression.Length && char.IsWhiteSpace(expression[pos])) pos++;
        }
    }

    public sealed class FastDataRelationCollection : IEnumerable<FastDataRelation>
    {
        private readonly FastDataSet _dataSet;
        private readonly List<FastDataRelation> _list;
        private readonly Dictionary<string, FastDataRelation> _byName;
        private readonly Dictionary<FastDataTable, List<FastDataRelation>> _byChildTable;

        internal FastDataRelationCollection(FastDataSet dataSet)
        {
            _dataSet = dataSet;
            _list = new List<FastDataRelation>(8);
            _byName = new Dictionary<string, FastDataRelation>(StringComparer.OrdinalIgnoreCase);
            _byChildTable = new Dictionary<FastDataTable, List<FastDataRelation>>();
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
            FastDataRelation rel = new FastDataRelation(relationName, parentColumn, childColumn);
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

