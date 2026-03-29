using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Game Save Convert Installer")]
[assembly: AssemblyDescription("Installer for Game Save Convert")]
[assembly: AssemblyCompany("SaveCompat")]
[assembly: AssemblyProduct("Game Save Convert Installer")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyFileVersion("4.0.0.0")]
[assembly: AssemblyVersion("4.0.0.0")]

class Installer
{
    static string installDir = @"C:\Tools\SaveCompat";

    static string profilesZipUrl = "https://github.com/mi5hmash/MandarinJuice/releases/download/v1.0.0/_profiles.zip";
    static string saveConvertZipUrl = "https://github.com/AlexeyGoto/game-save-convert/releases/latest/download/save-convert.zip";
    static string dotnetRuntimeUrl = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.4/windowsdesktop-runtime-10.0.4-win-x64.exe";
    static string readmeRawUrl = "https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/README.md";
    static string readmePath = @"C:\Tools\SaveCompat\README.md";

    static bool silentMode = false;

    // GUI elements
    static Label statusLabel;
    static ProgressBar progressBar;
    static Button installButton;
    static Form form;
    static TextBox logBox;

    [DllImport("kernel32.dll")]
    static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(uint dwProcessId);

    [STAThread]
    static int Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

        // Check for silent mode
        foreach (string arg in args)
        {
            string a = arg.ToLowerInvariant().TrimStart('-', '/');
            if (a == "s" || a == "silent" || a == "quiet" || a == "q")
                silentMode = true;
        }

        if (silentMode)
            return SilentInstall();

        // GUI mode: detach console (compiled as /target:exe for CMD compatibility)
        FreeConsole();

        // Check admin
        if (!IsAdmin())
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = String.Join(" ", args);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return 0;
            }
            catch
            {
                MessageBox.Show(
                    "\u0414\u043B\u044F \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438 \u0442\u0440\u0435\u0431\u0443\u044E\u0442\u0441\u044F \u043F\u0440\u0430\u0432\u0430 \u0430\u0434\u043C\u0438\u043D\u0438\u0441\u0442\u0440\u0430\u0442\u043E\u0440\u0430.",
                    "Game Save Convert", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        ShowGUI();
        return 0;
    }

    // ============================================================
    // Silent installation (CMD / SYSTEM account)
    // ============================================================
    static int SilentInstall()
    {
        Console.WriteLine();
        Console.WriteLine("===== Game Save Convert \u2014 Silent Install v4.0 =====");
        Console.WriteLine();

        try
        {
            DoInstall(
                (msg, pct) => { Console.WriteLine("[{0}%] {1}", pct, msg); },
                (msg) => { Console.WriteLine("  {0}", msg); }
            );

            Console.WriteLine();
            Console.WriteLine("===== INSTALLATION COMPLETE =====");
            Console.WriteLine("  Install dir: " + installDir);
            Console.WriteLine("  README:      " + readmePath);
            Console.WriteLine("  Restart terminal for PATH to take effect.");
            Console.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    // ============================================================
    // GUI
    // ============================================================
    static void ShowGUI()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        form = new Form();
        form.Text = "Game Save Convert \u2014 \u0423\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0430 v4.0";
        form.Size = new Size(550, 420);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;

        Label titleLabel = new Label();
        titleLabel.Text = "Game Save Convert v4.0";
        titleLabel.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        titleLabel.Location = new Point(20, 15);
        titleLabel.AutoSize = true;
        form.Controls.Add(titleLabel);

        Label descLabel = new Label();
        descLabel.Text = "\u0411\u044B\u0441\u0442\u0440\u0430\u044F \u043A\u043E\u043D\u0432\u0435\u0440\u0442\u0430\u0446\u0438\u044F \u0441\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0439 \u043C\u0435\u0436\u0434\u0443 Steam ID \u0431\u0435\u0437 \u043E\u0433\u0440\u0430\u043D\u0438\u0447\u0435\u043D\u0438\u0439";
        descLabel.Font = new Font("Segoe UI", 9);
        descLabel.ForeColor = Color.Gray;
        descLabel.Location = new Point(22, 48);
        descLabel.AutoSize = true;
        form.Controls.Add(descLabel);

        progressBar = new ProgressBar();
        progressBar.Location = new Point(20, 80);
        progressBar.Size = new Size(495, 25);
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;
        form.Controls.Add(progressBar);

        statusLabel = new Label();
        statusLabel.Text = "\u041D\u0430\u0436\u043C\u0438\u0442\u0435 \"\u0423\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u044C\" \u0434\u043B\u044F \u043D\u0430\u0447\u0430\u043B\u0430";
        statusLabel.Font = new Font("Segoe UI", 9);
        statusLabel.Location = new Point(20, 112);
        statusLabel.Size = new Size(495, 20);
        form.Controls.Add(statusLabel);

        logBox = new TextBox();
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.Font = new Font("Consolas", 8);
        logBox.Location = new Point(20, 138);
        logBox.Size = new Size(495, 188);
        logBox.BackColor = Color.FromArgb(30, 30, 30);
        logBox.ForeColor = Color.LightGreen;
        form.Controls.Add(logBox);

        installButton = new Button();
        installButton.Text = "\u0423\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u044C";
        installButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        installButton.Size = new Size(150, 35);
        installButton.Location = new Point(365, 338);
        installButton.Enabled = true;
        installButton.Click += OnInstallClick;
        form.Controls.Add(installButton);

        form.AcceptButton = installButton;
        Application.Run(form);
    }

    static bool IsAdmin()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    static void OnInstallClick(object sender, EventArgs e)
    {
        installButton.Enabled = false;
        Thread t = new Thread(GuiInstallThread);
        t.IsBackground = true;
        t.Start();
    }

    static void GuiInstallThread()
    {
        try
        {
            DoInstall(
                (msg, pct) =>
                {
                    form.Invoke((MethodInvoker)delegate
                    {
                        statusLabel.Text = msg;
                        progressBar.Value = Math.Min(pct, 100);
                    });
                },
                (msg) =>
                {
                    form.Invoke((MethodInvoker)delegate
                    {
                        logBox.AppendText(msg + Environment.NewLine);
                    });
                }
            );

            // Auto-open local README
            if (File.Exists(readmePath))
            {
                try { Process.Start(new ProcessStartInfo(readmePath) { UseShellExecute = true }); }
                catch { }
            }

            form.Invoke((MethodInvoker)delegate
            {
                statusLabel.Text = "\u0423\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0430 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0430!";
                progressBar.Value = 100;
                installButton.Text = "\u0417\u0430\u043A\u0440\u044B\u0442\u044C";
                installButton.Enabled = true;
                installButton.Click -= OnInstallClick;
                installButton.Click += (s, ev) => { form.Close(); };
            });
        }
        catch (Exception ex)
        {
            try
            {
                form.Invoke((MethodInvoker)delegate
                {
                    logBox.AppendText("\u041E\u0428\u0418\u0411\u041A\u0410: " + ex.Message + Environment.NewLine);
                    statusLabel.Text = "\u041E\u0448\u0438\u0431\u043A\u0430 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438";
                    installButton.Text = "\u041F\u043E\u0432\u0442\u043E\u0440\u0438\u0442\u044C";
                    installButton.Enabled = true;
                });
            }
            catch { }
        }
    }

    // ============================================================
    // Shared install logic
    // ============================================================

    static void DoInstall(Action<string, int> setStatus, Action<string> log)
    {
        // Use installDir for temp files (SYSTEM can't write to %TEMP% = C:\Windows\TEMP via some tools)
        string tmp = installDir;

        // Step 1: Create directories
        setStatus("\u0421\u043E\u0437\u0434\u0430\u043D\u0438\u0435 \u0434\u0438\u0440\u0435\u043A\u0442\u043E\u0440\u0438\u0439...", 5);
        log("[1/6] \u0421\u043E\u0437\u0434\u0430\u043D\u0438\u0435 \u0434\u0438\u0440\u0435\u043A\u0442\u043E\u0440\u0438\u0439...");
        string profDir = Path.Combine(installDir, @"mandarin\_profiles");
        if (!Directory.Exists(installDir)) Directory.CreateDirectory(installDir);
        if (!Directory.Exists(profDir)) Directory.CreateDirectory(profDir);
        log("  " + installDir);

        // Step 2: Download profiles
        if (Directory.GetFiles(profDir, "*.bin").Length == 0)
        {
            setStatus("\u0421\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435 \u043F\u0440\u043E\u0444\u0438\u043B\u0435\u0439 \u0438\u0433\u0440...", 10);
            log("[2/6] \u0421\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435 \u043F\u0440\u043E\u0444\u0438\u043B\u0435\u0439 \u0438\u0433\u0440...");
            string profZip = Path.Combine(tmp, "mandarin_profiles.zip");
            DownloadFile(profilesZipUrl, profZip);

            string tmpProf = Path.Combine(tmp, "mandarin_prof_extract");
            if (Directory.Exists(tmpProf)) Directory.Delete(tmpProf, true);
            ZipFile.ExtractToDirectory(profZip, tmpProf);

            string profSrc = FindDirectory(tmpProf, "_profiles");
            if (profSrc != null)
            {
                CopyDirectory(profSrc, profDir);
                log("  \u041F\u0440\u043E\u0444\u0438\u043B\u0438 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D\u044B: " + Directory.GetFiles(profDir, "*.bin").Length + " \u0448\u0442.");
            }
            else
            {
                log("  \u0412\u041D\u0418\u041C\u0410\u041D\u0418\u0415: \u043F\u0440\u043E\u0444\u0438\u043B\u0438 \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u044B \u0432 \u0430\u0440\u0445\u0438\u0432\u0435");
            }

            try { File.Delete(profZip); } catch { }
            try { Directory.Delete(tmpProf, true); } catch { }
        }
        else
        {
            log("[2/6] \u041F\u0440\u043E\u0444\u0438\u043B\u0438 \u0443\u0436\u0435 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D\u044B (" + Directory.GetFiles(profDir, "*.bin").Length + " \u0448\u0442.)");
        }

        // Step 3: Install .NET 10 Desktop Runtime
        setStatus("\u0423\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0430 .NET 10 Desktop Runtime...", 25);
        log("[3/6] .NET 10 Desktop Runtime...");

        if (IsDotnetDesktopInstalled())
        {
            log("  \u0423\u0436\u0435 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D");
        }
        else
        {
            bool installed = false;

            // Primary: download the actual Desktop Runtime installer and run silently
            log("  \u0421\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435 .NET 10 Desktop Runtime...");
            string runtimeExe = Path.Combine(tmp, "windowsdesktop-runtime-win-x64.exe");
            try
            {
                DownloadFile(dotnetRuntimeUrl, runtimeExe);
                setStatus("\u0423\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0430 .NET 10 Desktop Runtime...", 40);
                log("  \u0417\u0430\u043F\u0443\u0441\u043A \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438 (\u044D\u0442\u043E \u043C\u043E\u0436\u0435\u0442 \u0437\u0430\u043D\u044F\u0442\u044C 1-2 \u043C\u0438\u043D)...");
                int rc = RunProcess(runtimeExe, "/quiet /norestart", 300000);
                if (rc == 0 || rc == 3010) // 3010 = success, reboot required
                {
                    log("  .NET 10 Desktop Runtime \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D");
                    installed = true;
                }
                else
                {
                    log("  \u0423\u0441\u0442\u0430\u043D\u043E\u0432\u0449\u0438\u043A \u0432\u0435\u0440\u043D\u0443\u043B \u043A\u043E\u0434 " + rc + ", \u043F\u0440\u043E\u0431\u0443\u0435\u043C winget...");
                }
            }
            catch (Exception ex)
            {
                log("  \u041F\u0440\u044F\u043C\u0430\u044F \u0437\u0430\u0433\u0440\u0443\u0437\u043A\u0430 \u043D\u0435 \u0443\u0434\u0430\u043B\u0430\u0441\u044C: " + ex.Message);
            }
            try { File.Delete(runtimeExe); } catch { }

            // Fallback: winget
            if (!installed)
            {
                log("  \u041F\u043E\u043F\u044B\u0442\u043A\u0430 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438 \u0447\u0435\u0440\u0435\u0437 winget...");
                try
                {
                    int exitCode = RunProcess("winget",
                        "install Microsoft.DotNet.DesktopRuntime.10 --silent --scope machine --accept-source-agreements --accept-package-agreements",
                        300000);
                    if (exitCode == 0)
                    {
                        log("  .NET 10 Desktop Runtime \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D \u0447\u0435\u0440\u0435\u0437 winget");
                        installed = true;
                    }
                    else
                    {
                        log("  winget \u0432\u0435\u0440\u043D\u0443\u043B \u043A\u043E\u0434 " + exitCode);
                    }
                }
                catch
                {
                    log("  winget \u043D\u0435\u0434\u043E\u0441\u0442\u0443\u043F\u0435\u043D");
                }
            }

            if (!installed)
            {
                log("  \u041E\u0428\u0418\u0411\u041A\u0410: \u043D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u044C .NET 10");
                log("  \u0412\u0440\u0443\u0447\u043D\u0443\u044E: winget install Microsoft.DotNet.DesktopRuntime.10");
            }
        }

        // Step 4: Download save-convert.zip
        setStatus("\u0421\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435 save-convert...", 55);
        log("[4/6] \u0421\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435 save-convert...");
        string scZip = Path.Combine(tmp, "save-convert.zip");
        DownloadFile(saveConvertZipUrl, scZip);

        string tmpSc = Path.Combine(tmp, "save-convert-extract");
        if (Directory.Exists(tmpSc)) Directory.Delete(tmpSc, true);
        ZipFile.ExtractToDirectory(scZip, tmpSc);
        CopyDirectory(tmpSc, installDir);

        bool exeExists = File.Exists(Path.Combine(installDir, "save-convert.exe"));
        log("  save-convert \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D (exe: " + exeExists + ")");

        try { File.Delete(scZip); } catch { }
        try { Directory.Delete(tmpSc, true); } catch { }

        // Step 5: Add to PATH
        setStatus("\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0430 PATH...", 75);
        log("[5/6] \u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0430 PATH...");
        string machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        if (!machinePath.Contains(installDir))
        {
            Environment.SetEnvironmentVariable("Path", machinePath + ";" + installDir, EnvironmentVariableTarget.Machine);
            log("  \u0414\u043E\u0431\u0430\u0432\u043B\u0435\u043D\u043E \u0432 PATH: " + installDir);
        }
        else
        {
            log("  \u0423\u0436\u0435 \u0432 PATH");
        }

        // Step 6: Verify installation
        setStatus("\u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438...", 90);
        log("[6/6] \u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438...");
        VerifyInstallation(log);

        // Download README
        setStatus("\u0421\u043A\u0430\u0447\u0438\u0432\u0430\u043D\u0438\u0435 \u0438\u043D\u0441\u0442\u0440\u0443\u043A\u0446\u0438\u0438...", 95);
        try
        {
            DownloadFile(readmeRawUrl, readmePath);
            log("  \u0418\u043D\u0441\u0442\u0440\u0443\u043A\u0446\u0438\u044F: " + readmePath);
        }
        catch { log("  \u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0441\u043A\u0430\u0447\u0430\u0442\u044C README"); }

        // Done
        setStatus("\u0423\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0430 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043D\u0430!", 100);
        log("");
        log("===== \u0413\u041E\u0422\u041E\u0412\u041E =====");
        log("\u041F\u0435\u0440\u0435\u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u0435 \u0442\u0435\u0440\u043C\u0438\u043D\u0430\u043B \u0434\u043B\u044F \u043F\u0440\u0438\u043C\u0435\u043D\u0435\u043D\u0438\u044F PATH");
    }

    // ============================================================
    // Verification
    // ============================================================

    static void VerifyInstallation(Action<string> log)
    {
        string exePath = Path.Combine(installDir, "save-convert.exe");

        // Check exe exists
        if (!File.Exists(exePath))
        {
            log("  \u041E\u0428\u0418\u0411\u041A\u0410: save-convert.exe \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D \u0432 " + installDir);
            return;
        }

        // Check profiles exist
        string profDir = Path.Combine(installDir, @"mandarin\_profiles");
        int profCount = Directory.Exists(profDir) ? Directory.GetFiles(profDir, "*.bin").Length : 0;
        if (profCount == 0)
        {
            log("  \u041E\u0428\u0418\u0411\u041A\u0410: \u043F\u0440\u043E\u0444\u0438\u043B\u0438 \u0438\u0433\u0440 \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D\u044B \u0432 " + profDir);
            return;
        }

        // Try to run save-convert.exe benchmark (quick self-test)
        log("  \u0417\u0430\u043F\u0443\u0441\u043A \u0442\u0435\u0441\u0442\u0430 save-convert.exe benchmark...");
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.Arguments = "benchmark";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = installDir;

            Process p = Process.Start(psi);
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);

            if (p.ExitCode == 0)
            {
                // Extract speed line from output
                foreach (string line in stdout.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("Multi-thread") || trimmed.StartsWith("Estimated"))
                        log("  " + trimmed);
                }
                log("  \u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 \u043F\u0440\u043E\u0439\u0434\u0435\u043D\u0430 \u0443\u0441\u043F\u0435\u0448\u043D\u043E");
            }
            else
            {
                log("  \u041E\u0428\u0418\u0411\u041A\u0410: save-convert.exe \u0437\u0430\u0432\u0435\u0440\u0448\u0438\u043B\u0441\u044F \u0441 \u043A\u043E\u0434\u043E\u043C " + p.ExitCode);
                if (!string.IsNullOrWhiteSpace(stderr))
                    log("  stderr: " + stderr.Trim());
                if (!string.IsNullOrWhiteSpace(stdout))
                    log("  stdout: " + stdout.Trim());
                log("  \u0412\u043E\u0437\u043C\u043E\u0436\u043D\u043E \u043D\u0435 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D .NET 10 Desktop Runtime");
                log("  \u0423\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u0435 \u0432\u0440\u0443\u0447\u043D\u0443\u044E: winget install Microsoft.DotNet.DesktopRuntime.10");
            }
        }
        catch (Exception ex)
        {
            log("  \u041E\u0428\u0418\u0411\u041A\u0410 \u0437\u0430\u043F\u0443\u0441\u043A\u0430: " + ex.Message);
            log("  \u0412\u043E\u0437\u043C\u043E\u0436\u043D\u043E \u043D\u0435 \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D .NET 10 Desktop Runtime");
            log("  \u0423\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u0435 \u0432\u0440\u0443\u0447\u043D\u0443\u044E: winget install Microsoft.DotNet.DesktopRuntime.10");
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    static bool IsDotnetDesktopInstalled()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "dotnet";
            psi.Arguments = "--list-runtimes";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return output.Contains("Microsoft.WindowsDesktop.App 10.");
        }
        catch { return false; }
    }

    static void DownloadFile(string url, string path)
    {
        using (WebClient wc = new WebClient())
        {
            wc.DownloadFile(url, path);
        }
    }

    static int RunProcess(string fileName, string arguments, int timeoutMs)
    {
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = fileName;
        psi.Arguments = arguments;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        Process p = Process.Start(psi);
        p.WaitForExit(timeoutMs);
        return p.ExitCode;
    }

    static string FindDirectory(string root, string name)
    {
        foreach (string d in Directory.GetDirectories(root, name, SearchOption.AllDirectories))
            return d;
        return null;
    }

    static void CopyDirectory(string src, string dst)
    {
        if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(src))
            CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
