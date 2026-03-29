namespace SaveConvert;

/// <summary>
/// WinForms progress window shown only during full brute-force search.
/// No close button (X). Has a Cancel button instead.
/// Thread-safe updates via Invoke.
/// </summary>
sealed class ProgressForm : Form
{
    readonly Label _titleLabel;
    readonly Label _subtitleLabel;
    readonly Label _noteLabel;
    readonly ProgressBar _progressBar;
    readonly Label _speedLabel;
    readonly Label _remainingLabel;
    readonly Label _checkedLabel;
    readonly Button _cancelButton;
    readonly CancellationTokenSource _cts = new();
    bool _allowClose;

    public CancellationToken Token => _cts.Token;

    public ProgressForm()
    {
        Text = "Game Save Convert — Перенос сохранений";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 310);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);

        _titleLabel = new Label
        {
            Text = "Идёт одноразовая настройка переноса сохранений",
            Location = new Point(24, 20),
            Size = new Size(552, 28),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 30)
        };
        Controls.Add(_titleLabel);

        _subtitleLabel = new Label
        {
            Text = "Эта операция выполняется только один раз.",
            Location = new Point(24, 52),
            Size = new Size(552, 20),
            ForeColor = Color.FromArgb(100, 100, 100)
        };
        Controls.Add(_subtitleLabel);

        _noteLabel = new Label
        {
            Text = "В дальнейшем перенос будет мгновенным.",
            Location = new Point(24, 72),
            Size = new Size(552, 20),
            ForeColor = Color.FromArgb(100, 100, 100)
        };
        Controls.Add(_noteLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(24, 108),
            Size = new Size(552, 28),
            Minimum = 0,
            Maximum = 1000,
            Style = ProgressBarStyle.Continuous
        };
        Controls.Add(_progressBar);

        _speedLabel = new Label
        {
            Text = "Скорость: —",
            Location = new Point(24, 150),
            Size = new Size(552, 22),
            ForeColor = Color.FromArgb(50, 50, 50)
        };
        Controls.Add(_speedLabel);

        _remainingLabel = new Label
        {
            Text = "Осталось: —",
            Location = new Point(24, 174),
            Size = new Size(552, 22),
            ForeColor = Color.FromArgb(50, 50, 50)
        };
        Controls.Add(_remainingLabel);

        _checkedLabel = new Label
        {
            Text = "Проверено: 0 из 4 294 967 295",
            Location = new Point(24, 198),
            Size = new Size(552, 22),
            ForeColor = Color.FromArgb(50, 50, 50)
        };
        Controls.Add(_checkedLabel);

        _cancelButton = new Button
        {
            Text = "Отмена",
            Size = new Size(120, 36),
            Location = new Point(456, 256),
            Font = new Font("Segoe UI", 9.5f),
            FlatStyle = FlatStyle.System
        };
        _cancelButton.Click += (_, _) =>
        {
            _cancelButton.Enabled = false;
            _cancelButton.Text = "Отмена...";
            _cts.Cancel();
        };
        Controls.Add(_cancelButton);

        FormClosing += (_, e) =>
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
        };
    }

    public void UpdateProgress(long checkedCount, long totalCount, double rate)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            Invoke(() =>
            {
                double pct = (double)checkedCount / totalCount;
                _progressBar.Value = Math.Min(1000, (int)(pct * 1000));
                _speedLabel.Text = $"Скорость: {rate:N0} ID/сек";
                if (rate > 0)
                {
                    double remainingSec = (totalCount - checkedCount) / rate;
                    var ts = TimeSpan.FromSeconds(remainingSec);
                    _remainingLabel.Text = $"Осталось: ~{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
                }
                _checkedLabel.Text = $"Проверено: {checkedCount:N0} из {totalCount:N0}";
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void ShowDone(string resultText)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            Invoke(() =>
            {
                _progressBar.Value = 1000;
                _speedLabel.Text = resultText;
                _remainingLabel.Text = "";
                _cancelButton.Text = "Готово";
                _cancelButton.Enabled = true;
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void CloseFromThread()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            Invoke(() =>
            {
                _allowClose = true;
                Close();
            });
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}
