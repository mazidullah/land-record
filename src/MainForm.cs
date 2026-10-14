using System.Data;
using Microsoft.Data.Sqlite;

namespace LandRecords;

public class MainForm : Form
{
    // SQLite column name, Bengali label
    private static readonly (string Col, string Label)[] Fields =
    {
        ("name",     "নাম"),
        ("village",  "গ্রাম"),
        ("mouza",    "মৌজা"),
        ("ward",     "ওয়ার্ড"),
        ("khatian",  "খতিয়ান"),
        ("dag_no",   "দাগ নাম্বার"),
        ("quantity", "পরিমান"),
        ("holding",  "হোল্ডিং"),
    };

    private readonly TextBox[] _boxes = new TextBox[Fields.Length];
    private readonly TextBox _txtSearch = new();
    private readonly DataGridView _grid = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnDelete = new();
    private readonly Label _lblStatus = new();

    private readonly string _dbPath;
    private readonly string _connStr;
    private long _currentId = -1;      // -1 = adding a new record
    private bool _binding;             // true while the grid is being refilled

    public MainForm()
    {
        Text = "ভূমি রেকর্ড ব্যবস্থাপনা";
        Font = new Font("Nirmala UI", 11f);
        Size = new Size(1150, 720);
        StartPosition = FormStartPosition.CenterScreen;

        string dir = Path.Combine(
            Directory.GetCurrentDirectory(), "Database");

        //string dir = Path.Combine(
        //    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LandRecords");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "records.db");
        _connStr = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();

        InitDatabase();
        BuildUi();
        RefreshGrid();
    }

    // ------------------------------------------------------------ Database
    private void InitDatabase()
    {
        string cols = string.Join(", ", Fields.Select(f => $"{f.Col} TEXT NOT NULL DEFAULT ''"));
        using var con = new SqliteConnection(_connStr);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"CREATE TABLE IF NOT EXISTS records (id INTEGER PRIMARY KEY AUTOINCREMENT, {cols})";
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ UI
    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // form
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // search
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // grid
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // status

        // ---- Entry form: 4 columns (label, box, label, box) x 4 rows
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 4,
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        int perColumn = Fields.Length / 2;
        for (int i = 0; i < Fields.Length; i++)
        {
            int col = i < perColumn ? 0 : 2;
            int row = i % perColumn;

            var lbl = new Label
            {
                Text = Fields[i].Label + ":",
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 8, 8, 3),
            };
            var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 5, 20, 5) };
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveRecord(); }
            };
            _boxes[i] = box;

            form.Controls.Add(lbl, col, row);
            form.Controls.Add(box, col + 1, row);
        }
        root.Controls.Add(form, 0, 0);

        // ---- Buttons
        _btnSave.Text = "যোগ করুন";
        _btnClear.Text = "নতুন / পরিষ্কার";
        _btnDelete.Text = "মুছুন";
        _btnDelete.Enabled = false;
        foreach (var b in new[] { _btnSave, _btnClear, _btnDelete })
        {
            b.AutoSize = true;
            b.Padding = new Padding(8, 3, 8, 3);
        }
        _btnSave.Click += (_, _) => SaveRecord();
        _btnClear.Click += (_, _) => ClearForm();
        _btnDelete.Click += (_, _) => DeleteRecord();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { _btnSave, _btnClear, _btnDelete });
        root.Controls.Add(buttons, 0, 1);

        // ---- Search
        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 10, 0, 6),
        };
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchPanel.Controls.Add(new Label
        {
            Text = "অনুসন্ধান:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 8, 3),
        }, 0, 0);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "নাম, গ্রাম, মৌজা, ওয়ার্ড, খতিয়ান, দাগ, পরিমান বা হোল্ডিং দিয়ে খুঁজুন...";
        _txtSearch.TextChanged += (_, _) => RefreshGrid();
        searchPanel.Controls.Add(_txtSearch, 1, 0);
        root.Controls.Add(searchPanel, 0, 2);

        // ---- Grid
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
        _grid.BackgroundColor = SystemColors.Window;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.SelectionChanged += Grid_SelectionChanged;
        root.Controls.Add(_grid, 0, 3);

        // ---- Status
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = Color.DimGray;
        root.Controls.Add(_lblStatus, 0, 4);

        Controls.Add(root);
    }

    // --------------------------------------------------------- Data actions
    private void RefreshGrid()
    {
        var words = _txtSearch.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        string select = "SELECT id, " + string.Join(", ", Fields.Select(f => f.Col)) + " FROM records";

        // Every typed word must match at least one column (AND across words, OR across columns)
        var wordClauses = new List<string>();
        for (int w = 0; w < words.Length; w++)
        {
            var cols = Fields.Select(f => $"{f.Col} LIKE @w{w} ESCAPE '\\'");
            wordClauses.Add("(" + string.Join(" OR ", cols) + ")");
        }
        if (wordClauses.Count > 0)
            select += " WHERE " + string.Join(" AND ", wordClauses);
        select += " ORDER BY id DESC";

        var table = new DataTable();
        long total;
        using (var con = new SqliteConnection(_connStr))
        {
            con.Open();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = select;
                for (int w = 0; w < words.Length; w++)
                {
                    string esc = words[w].Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                    cmd.Parameters.AddWithValue($"@w{w}", "%" + esc + "%");
                }
                using var reader = cmd.ExecuteReader();
                table.Load(reader);
            }
            using var cnt = con.CreateCommand();
            cnt.CommandText = "SELECT COUNT(*) FROM records";
            total = (long)(cnt.ExecuteScalar() ?? 0L);
        }

        _binding = true;
        _grid.DataSource = table;
        _grid.Columns["id"]!.Visible = false;
        for (int i = 0; i < Fields.Length; i++)
            _grid.Columns[Fields[i].Col]!.HeaderText = Fields[i].Label;
        _grid.ClearSelection();
        _binding = false;

        _lblStatus.Text = $"দেখানো হচ্ছে {table.Rows.Count} / মোট {total} টি রেকর্ড    |    ডাটাবেস: {_dbPath}";
    }

    private void Grid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_binding || _grid.SelectedRows.Count == 0) return;

        var row = _grid.SelectedRows[0];
        _currentId = Convert.ToInt64(row.Cells["id"].Value);
        for (int i = 0; i < Fields.Length; i++)
            _boxes[i].Text = Convert.ToString(row.Cells[Fields[i].Col].Value) ?? "";

        _btnSave.Text = "আপডেট করুন";
        _btnDelete.Enabled = true;
    }

    private void ClearForm()
    {
        _currentId = -1;
        foreach (var b in _boxes) b.Clear();
        _grid.ClearSelection();
        _btnSave.Text = "যোগ করুন";
        _btnDelete.Enabled = false;
        _boxes[0].Focus();
    }

    private void SaveRecord()
    {
        if (string.IsNullOrWhiteSpace(_boxes[0].Text))
        {
            MessageBox.Show("অনুগ্রহ করে নাম লিখুন।", "তথ্য প্রয়োজন",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _boxes[0].Focus();
            return;
        }

        try
        {
            using var con = new SqliteConnection(_connStr);
            con.Open();
            using var cmd = con.CreateCommand();

            if (_currentId < 0)
            {
                string cols = string.Join(",", Fields.Select(f => f.Col));
                string pars = string.Join(",", Fields.Select(f => "@" + f.Col));
                cmd.CommandText = $"INSERT INTO records ({cols}) VALUES ({pars})";
            }
            else
            {
                string sets = string.Join(",", Fields.Select(f => $"{f.Col}=@{f.Col}"));
                cmd.CommandText = $"UPDATE records SET {sets} WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", _currentId);
            }
            for (int i = 0; i < Fields.Length; i++)
                cmd.Parameters.AddWithValue("@" + Fields[i].Col, _boxes[i].Text.Trim());

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Database error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ClearForm();
        RefreshGrid();
    }

    private void DeleteRecord()
    {
        if (_currentId < 0) return;
        if (MessageBox.Show("এই রেকর্ডটি কি মুছে ফেলতে চান?", "নিশ্চিত করুন",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            using var con = new SqliteConnection(_connStr);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM records WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", _currentId);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Database error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ClearForm();
        RefreshGrid();
    }
}
