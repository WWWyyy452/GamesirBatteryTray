namespace GamesirBatteryTray;

/// <summary>Small modal prompt for renaming a controller.</summary>
internal sealed class RenameDialog : Form
{
    private readonly TextBox _input;

    public RenameDialog(string currentName, string mac)
    {
        Text = "重命名手柄";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(340, 130);
        Font = new Font("Microsoft YaHei UI", 9f);

        var label = new Label
        {
            Text = $"设备 {mac}",
            AutoSize = true,
            Location = new Point(16, 14),
            ForeColor = SystemColors.GrayText
        };

        _input = new TextBox
        {
            Text = currentName,
            Location = new Point(16, 40),
            Width = 308,
            MaxLength = 16
        };

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(168, 82), Width = 74 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(250, 82), Width = 74 };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { label, _input, ok, cancel });

        _input.SelectAll();
    }

    public string Value => _input.Text.Trim();
}
