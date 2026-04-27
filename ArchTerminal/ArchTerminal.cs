using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArchTerminal
{
    class Terminal
    {
        private static readonly string DataFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shortcuts.json");
        private static Dictionary<string, string> _apps = new();
        private static string _username = Environment.UserName;
        private static string _hostname = Environment.MachineName;
        private static string _cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static CancellationTokenSource _updateCts = new CancellationTokenSource();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static (long usedMB, long totalMB) GetPhysicalMemory()
        {
            try
            {
                MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    long total = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                    long available = (long)(memStatus.ullAvailPhys / (1024 * 1024));
                    return (total - available, total);
                }
            }
            catch { }
            return (0, 0);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        const int STD_OUTPUT_HANDLE = -11;
        const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        static void EnableAnsiColors()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (GetConsoleMode(handle, out uint mode))
                    SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
            }
            catch { }
        }

        static void Main()
        {
            EnableAnsiColors();
            Console.OutputEncoding = Encoding.UTF8;

            Console.CancelKeyPress += (sender, e) => {
                e.Cancel = true;
                _updateCts.Cancel();
                Environment.Exit(0);
            };

            try { Console.SetWindowSize(Math.Max(Console.WindowWidth, 100), Math.Max(Console.WindowHeight, 35)); }
            catch { }

            LoadShortcuts();
            InitializeCommands();
            ShowNeofetch();

            Task.Run(() => LiveTitleUpdater(_updateCts.Token));

            while (true)
            {
                ShowPrompt();
                string? input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) continue;
                ProcessCommand(input);
            }
        }

        static void LiveTitleUpdater(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(1000);
                    if (token.IsCancellationRequested) break;
                    string uptime = GetUptime();
                    var (usedMem, totalMem) = GetPhysicalMemory();
                    Console.Title = $"ArchTerminal | {_username}@{_hostname} | Uptime: {uptime} | RAM: {usedMem}/{totalMem} MiB";
                }
                catch { break; }
            }
        }

        // ═══════════════════════════════════════════════════════
        //  شعار Arch Linux الكلاسيكي
        // ═══════════════════════════════════════════════════════
        static (string line, string color)[] GetArchLogo()
        {
            return new (string, string)[]
            {
                ("                  -`",                                    "36"),
                ("                  .o+`",                                  "36"),
                ("                 `ooo/",                                  "36"),
                ("                `+oooo:",                                 "36"),
                ("               `+oooooo:",                                "36"),
                ("               -+oooooo+:",                               "36"),
                ("             `/:-:++oooo+:",                              "36"),
                ("            `/++++/+++++++:",                             "36"),
                ("           `/++++++++++++++:",                            "36"),
                ("          `/+++ooooooooooooo/`",                           "36"),
                ("         ./ooosssso++osssssso+`",                         "36"),
                ("        .oossssso-````/ossssss+`",                        "36"),
                ("       -osssssso.      :ssssssso.",                       "36"),
                ("      :osssssss/        osssso+++.",                      "36"),
                ("     /ossssssss/        +ssssooo/-",                      "36"),
                ("   `/ossssso+/:-        -:/+osssso+-",                    "36"),
                ("  `+sso+:-`                 `.-/+oso:",                   "36"),
                (" `++:.                           `-/+/",                  "36"),
                (" .`                                 `",                   "36"),
            };
        }

        // ═══════════════════════════════════════════════════════
        //  واجهة Neofetch
        // ═══════════════════════════════════════════════════════
        static void ShowNeofetch()
        {
            Console.Clear();

            var logo = GetArchLogo();
            string os = "Windows " + Environment.OSVersion.Version.ToString(3);
            string kernel = Environment.OSVersion.Version.ToString();
            string cpu = GetCpuName();
            var (usedMem, totalMem) = GetPhysicalMemory();

            string[] info = {
                $"\x1b[1;36m{_username}@{_hostname}\x1b[0m",
                "\x1b[1;36m" + new string('─', Math.Min(_username.Length + _hostname.Length + 1, 20)) + "\x1b[0m",
                $"\x1b[1;36mOS:\x1b[0m       {os}",
                $"\x1b[1;36mHost:\x1b[0m     {_hostname}",
                $"\x1b[1;36mKernel:\x1b[0m   {kernel}",
                $"\x1b[1;36mUptime:\x1b[0m   {GetUptime()}",
                $"\x1b[1;36mShell:\x1b[0m    arch omer terminal" ,
                $"\x1b[1;36mCPU:\x1b[0m      {cpu}",
                $"\x1b[1;36mMemory:\x1b[0m   {usedMem}MiB / {totalMem}MiB",
                "",
                GetColorBlocks(),
                "",
                "made with love by omer suqi - github.com/ketome"
            };

            int maxLines = Math.Max(logo.Length, info.Length);
            int logoWidth = 36;

            Console.WriteLine();
            for (int i = 0; i < maxLines; i++)
            {
                if (i < logo.Length)
                {
                    string left = logo[i].line;
                    string color = logo[i].color;
                    Console.Write($"\x1b[1;{color}m");
                    Console.Write(left.PadRight(logoWidth));
                    Console.Write("\x1b[0m");
                }
                else
                {
                    Console.Write(new string(' ', logoWidth));
                }

                Console.Write("   ");

                if (i < info.Length)
                    Console.WriteLine(info[i]);
                else
                    Console.WriteLine();
            }

            Console.WriteLine();
            Console.ResetColor();
        }

        static string GetColorBlocks()
        {
            string[] codes = { "40", "41", "42", "43", "44", "45", "46", "47" };
            string[] fgCodes = { "37", "30", "30", "30", "37", "37", "30", "30" };
            var blocks = new List<string>();
            for (int i = 0; i < codes.Length; i++)
            {
                blocks.Add($"\x1b[{fgCodes[i]};{codes[i]}m   \x1b[0m");
            }
            return string.Join("", blocks);
        }

        static string GetUptime()
        {
            var ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (ts.Days > 0) return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m";
            if (ts.Hours > 0) return $"{ts.Hours}h {ts.Minutes}m";
            return $"{ts.Minutes} mins";
        }

        static string GetCpuName()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var p = new Process { StartInfo = new ProcessStartInfo("wmic", "cpu get Name")
                        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } };
                    p.Start();
                    string out_ = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    var lines = out_.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length >= 2) return lines[1].Trim();
                }
            }
            catch { }
            return RuntimeInformation.ProcessArchitecture.ToString();
        }

        static void ShowPrompt()
        {
            string shortPath = _cwd.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)
                ? "~" : Path.GetFileName(_cwd);
            Console.Write($"\x1b[1;36m[{_username}@{_hostname} \x1b[1;37m{shortPath}\x1b[1;36m]\x1b[0m$ ");
        }

        private static Dictionary<string, Action<string[]>> commands = new();

        static void InitializeCommands()
        {
            // =================== 1. أوامر الملفات والمجلدات ===================
            commands["ls"] = args => RunCmd("ls", args);
            commands["dir"] = args => RunCmd("dir", args);
            commands["cd"] = args => ChangeDir(args);
            commands["pwd"] = _ => Console.WriteLine(_cwd);
            commands["mkdir"] = args => DirAction(args, false);
            commands["rmdir"] = args => DirAction(args, true);
            commands["touch"] = args => { if(args.Length>0) File.Create(Path.Combine(_cwd, args[0])).Close(); };
            commands["cat"] = args => { foreach(var f in args) { string p=Path.Combine(_cwd,f); if(File.Exists(p)) Console.WriteLine(File.ReadAllText(p)); else PrintError($"cat: {f}: No such file"); }};
            commands["cp"] = args => { if(args.Length>=2) File.Copy(Path.Combine(_cwd,args[0]), Path.Combine(_cwd,args[1]), true); };
            commands["mv"] = args => { if(args.Length>=2) File.Move(Path.Combine(_cwd,args[0]), Path.Combine(_cwd,args[1])); };
            commands["rm"] = args => { foreach(var f in args) try{File.Delete(Path.Combine(_cwd,f));}catch{} };
            commands["find"] = args => { if(args.Length>0) Directory.GetFiles(_cwd, args[0], SearchOption.AllDirectories).ToList().ForEach(Console.WriteLine); };
            commands["grep"] = args => { if(args.Length>=2) foreach(var l in File.ReadAllLines(Path.Combine(_cwd,args[1]))) if(l.Contains(args[0])) Console.WriteLine(l); };
            commands["wc"] = args => { if(args.Length>0){var c=File.ReadAllText(Path.Combine(_cwd,args[0])); Console.WriteLine($"{c.Split('\n').Length} {c.Split(' ').Length} {c.Length} {args[0]}");}};
            commands["head"] = args => PrintLines(args, true);
            commands["tail"] = args => PrintLines(args, false);
            commands["stat"] = args => { if(args.Length>0){var f=new FileInfo(Path.Combine(_cwd,args[0])); Console.WriteLine($"Size: {f.Length}\nModified: {f.LastWriteTime}");}};

            // =================== 2. أوامر النظام والعمليات ===================
            commands["ps"] = _ => Process.GetProcesses().OrderBy(p=>p.ProcessName).Take(25).ToList().ForEach(p => Console.WriteLine($"{p.Id,-8}{p.ProcessName}"));
            commands["kill"] = args => { if(args.Length>0 && int.TryParse(args[0], out int pid)) try{Process.GetProcessById(pid).Kill(); PrintSuccess($"Killed {pid}");}catch{} };
            commands["top"] = _ => RunCmd("tasklist", Array.Empty<string>());
            commands["free"] = _ => { var (u,t)=GetPhysicalMemory(); Console.WriteLine($"Mem: {t}MB Total, {u}MB Used, {t-u}MB Free"); };
            commands["uname"] = args => Console.WriteLine(RuntimeInformation.OSDescription);
            commands["uptime"] = _ => Console.WriteLine(GetUptime());
            commands["date"] = _ => Console.WriteLine(DateTime.Now);
            commands["cal"] = _ => PrintCalendar();
            commands["whoami"] = _ => Console.WriteLine(_username);
            commands["hostname"] = _ => Console.WriteLine(_hostname);
            commands["sleep"] = args => { if(args.Length>0 && double.TryParse(args[0], out double s)) Thread.Sleep((int)(s*1000)); };

            // =================== 3. أوامر الشبكة ===================
            commands["ping"] = args => RunCmd("ping", args);
            commands["netstat"] = args => RunCmd("netstat", args);
            commands["ifconfig"] = args => RunCmd("ipconfig", args);
            commands["curl"] = args => RunCmd("curl", args);
            commands["nslookup"] = args => RunCmd("nslookup", args);

            // =================== 4. التحكم بالواجهة والنظام ===================
            commands["clear"] = _ => Console.Clear();
            commands["cls"] = commands["clear"];
            commands["neofetch"] = _ => ShowNeofetch();
            commands["exit"] = _ => { _updateCts.Cancel(); Environment.Exit(0); };
            commands["quit"] = commands["exit"];
            commands["echo"] = args => Console.WriteLine(string.Join(" ", args));
            commands["export"] = args => { var p=args[0].Split('=',2); if(p.Length==2) Environment.SetEnvironmentVariable(p[0],p[1]); };
            commands["alias"] = args => { var p=args[0].Split('=',2); if(p.Length==2) commands[p[0]]=_=>ProcessCommand(p[1]); };

            // =================== 5. أوامر الترفيه والتطبيقات ===================
            commands["cowsay"] = args => Cowsay(args);
            commands["cmatrix"] = _ => RunCMatrix();
            commands["apps"] = _ => HandleListApps();
            commands["help"] = _ => ShowHelp();
            commands["yes"] = args => { string w = args.Length>0?string.Join(" ",args):"y"; while(!Console.KeyAvailable) Console.WriteLine(w); };

            // =================== 6. أوامر تطوير الويب والبرمجة (Node.js, Python, Git) ===================
            commands["node"] = args => RunCmd("node", args);
            commands["npm"] = args => RunCmd("npm", args);
            commands["npx"] = args => RunCmd("npx", args);
            commands["pnpm"] = args => RunCmd("pnpm", args);
            commands["yarn"] = args => RunCmd("yarn", args);
            
            commands["python"] = args => RunCmd("python", args);
            commands["python3"] = args => RunCmd("python3", args);
            commands["py"] = args => RunCmd("py", args);
            commands["pip"] = args => RunCmd("pip", args);
            
            commands["git"] = args => RunCmd("git", args);
            commands["code"] = args => RunCmd("code", args);
            commands["dotnet"] = args => RunCmd("dotnet", args);
            commands["cargo"] = args => RunCmd("cargo", args);
            commands["go"] = args => RunCmd("go", args);
            commands["gcc"] = args => RunCmd("gcc", args);

            // =================== 7. أوامر Node.js مخصصة وسريعة ===================
            commands["init-node"] = args => {
                if (!File.Exists(Path.Combine(_cwd, "package.json"))) {
                    RunCmd("npm", new[] { "init", "-y" });
                }
                if (!File.Exists(Path.Combine(_cwd, "index.js"))) {
                    File.WriteAllText(Path.Combine(_cwd, "index.js"), "console.log('Hello from ArchTerminal!');\n");
                    PrintSuccess("Created index.js");
                } else {
                    PrintInfo("index.js already exists.");
                }
            };

            commands["install"] = args => {
                var newArgs = new List<string> { "install" };
                newArgs.AddRange(args);
                RunCmd("npm", newArgs.ToArray());
            };

            commands["run-script"] = args => {
                if (args.Length > 0) RunCmd("npm", new[] { "run", args[0] });
                else PrintError("Usage: run-script <script_name>");
            };

            commands["start"] = args => RunCmd("npm", new[] { "start" });
        }

        static void ProcessCommand(string input)
        {
            string lower = input.ToLower().Trim();

            if (lower.StartsWith("set shortcut apps ")) { HandleSetShortcut(input.Substring("set shortcut apps ".Length)); return; }
            if (lower.StartsWith("open app ")) { HandleOpenApp(input.Substring("open app ".Length).Trim()); return; }
            if (lower.StartsWith("remove app ")) { HandleRemoveApp(input.Substring("remove app ".Length).Trim()); return; }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string cmd = parts[0].ToLower();
            string[] args = parts.Skip(1).ToArray();

            if (commands.ContainsKey(cmd))
            {
                try { commands[cmd](args); }
                catch (Exception ex) { PrintError(ex.Message); }
            }
            else
            {
                RunCmd(parts[0], args);
            }
        }

        static void ChangeDir(string[] args)
        {
            string target = args.Length > 0 ? args[0] : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (target == "~") { _cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); Environment.CurrentDirectory = _cwd; return; }
            string newPath = Path.GetFullPath(Path.Combine(_cwd, target));
            if (Directory.Exists(newPath)) { _cwd = newPath; Environment.CurrentDirectory = _cwd; }
            else PrintError($"cd: No such directory: {target}");
        }

        static void DirAction(string[] args, bool remove)
        {
            if (args.Length == 0) return;
            string path = Path.Combine(_cwd, args[0]);
            try { if (remove) Directory.Delete(path, false); else Directory.CreateDirectory(path); }
            catch (Exception ex) { PrintError(ex.Message); }
        }

        static void PrintLines(string[] args, bool head)
        {
            if (args.Length == 0) return;
            string file = Path.Combine(_cwd, args[0]);
            if (!File.Exists(file)) { PrintError($"File not found: {args[0]}"); return; }
            var lines = File.ReadAllLines(file);
            int count = 10;
            if (args.Length > 1) int.TryParse(args[1], out count);
            var selected = head ? lines.Take(count) : lines.Skip(Math.Max(0, lines.Length - count));
            foreach (var l in selected) Console.WriteLine(l);
        }

        static void PrintCalendar()
        {
            var now = DateTime.Now;
            Console.WriteLine($"\x1b[1;36m     {now:MMMM yyyy}\x1b[0m");
            Console.WriteLine("Su Mo Tu We Th Fr Sa");
            int first = (int)new DateTime(now.Year, now.Month, 1).DayOfWeek;
            int days = DateTime.DaysInMonth(now.Year, now.Month);
            int day = 1;
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    if (i == 0 && j < first) Console.Write("   ");
                    else if (day > days) break;
                    else { if (day == now.Day) Console.Write($"\x1b[1;36m{day,2}\x1b[0m "); else Console.Write($"{day,2} "); }
                    day++;
                }
                Console.WriteLine();
                if (day > days) break;
            }
        }

        static void Cowsay(string[] args)
        {
            string msg = args.Length > 0 ? string.Join(" ", args) : "Moo!";
            int len = msg.Length + 2;
            Console.WriteLine($" {new string('_', len)}");
            Console.WriteLine($"< {msg} >");
            Console.WriteLine($" {new string('-', len)}");
            Console.WriteLine(@"        \   ^__^");
            Console.WriteLine(@"         \  (oo)\_______");
            Console.WriteLine(@"            (__)\       )\/\");
            Console.WriteLine(@"                ||----w |");
            Console.WriteLine(@"                ||     ||");
        }

        static void RunCMatrix()
        {
            Console.Clear();
            Console.CursorVisible = false;
            Random r = new Random();
            char[] chars = "01アイウエオカキクケコサシスセソ".ToCharArray();
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            int[] drops = new int[width];
            for (int i = 0; i < width; i++) drops[i] = r.Next(height);

            while (!Console.KeyAvailable)
            {
                for (int i = 0; i < width; i++)
                {
                    if (drops[i] >= 0 && drops[i] < height)
                    {
                        try
                        {
                            Console.SetCursorPosition(i, drops[i]);
                            Console.ForegroundColor = drops[i] % 3 == 0 ? ConsoleColor.White : ConsoleColor.Green;
                            Console.Write(chars[r.Next(chars.Length)]);
                            Console.ResetColor();
                        }
                        catch { }
                    }
                    drops[i]++;
                    if (drops[i] > height && r.NextDouble() > 0.975) drops[i] = 0;
                }
                Thread.Sleep(50);
            }
            Console.ReadKey(true);
            Console.CursorVisible = true;
            ShowNeofetch();
        }

        static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("\x1b[1;36m╔══════════════════════════════════════════════════════════╗\x1b[0m");
            Console.WriteLine("\x1b[1;36m║           ArchTerminal - Built-in Commands              ║\x1b[0m");
            Console.WriteLine("\x1b[1;36m╠══════════════════════════════════════════════════════════╣\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33m[Files]   \x1b[0mls dir cd pwd mkdir rmdir touch cat cp mv rm\x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m           find grep wc head tail stat                    \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33m[System]  \x1b[0mps kill top free uname uptime date cal whoami\x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m           hostname sleep                                   \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33m[Network] \x1b[0mping netstat ifconfig curl nslookup            \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33m[Control] \x1b[0mclear cls neofetch exit echo export set alias\x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33m[Dev]     \x1b[0mnode npm npx python git code dotnet cargo  \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m           init-node install run-script start               \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33m[Apps]    \x1b[0mapps set shortcut apps <n> <p>              \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m           open app <n> remove app <n>                    \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33m[Fun]     \x1b[0mcowsay cmatrix yes                               \x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m╚══════════════════════════════════════════════════════════╝\x1b[0m");
            Console.WriteLine();
        }

        static void LoadShortcuts()
        {
            try { if (File.Exists(DataFile)) _apps = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(DataFile)) ?? new(); }
            catch { _apps = new(); }
        }

        static void SaveShortcuts()
        {
            try { File.WriteAllText(DataFile, JsonSerializer.Serialize(_apps, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }

        static void HandleSetShortcut(string args)
        {
            var parts = args.Split(' ', 2);
            if (parts.Length == 2) { _apps[parts[0]] = parts[1]; SaveShortcuts(); PrintSuccess($"App '{parts[0]}' saved."); }
            else PrintError("Usage: set shortcut apps <name> <path>");
        }

        static void HandleOpenApp(string name)
        {
            if (_apps.ContainsKey(name)) { try { Process.Start(new ProcessStartInfo(_apps[name]) { UseShellExecute = true }); PrintSuccess($"Opening '{name}'..."); } catch { PrintError("Failed to open app."); } }
            else PrintError($"App '{name}' not found. Type 'apps' to list.");
        }

        static void HandleListApps()
        {
            if (_apps.Count == 0) { PrintInfo("No apps saved. Use: set shortcut apps <name> <path>"); return; }
            Console.WriteLine("\n\x1b[1;36m╔══════════════════════════════════════════════════╗\x1b[0m");
            Console.WriteLine("\x1b[1;36m║\x1b[0m \x1b[1;33mSaved Apps/Shortcuts:\x1b[0m" + new string(' ', 33) + "\x1b[1;36m║\x1b[0m");
            Console.WriteLine("\x1b[1;36m╠══════════════════════════════════════════════════╣\x1b[0m");
            foreach (var app in _apps)
            {
                string line = $"  {app.Key,-12} → {app.Value}";
                Console.WriteLine("\x1b[1;36m║\x1b[0m" + line.PadRight(52) + "\x1b[1;36m║\x1b[0m");
            }
            Console.WriteLine("\x1b[1;36m╚══════════════════════════════════════════════════╝\x1b[0m\n");
        }

        static void HandleRemoveApp(string name)
        {
            if (_apps.Remove(name)) { SaveShortcuts(); PrintSuccess($"Removed '{name}'"); }
            else PrintError($"App '{name}' not found.");
        }

        // ═══════════════════════════════════════════════════════
        //  تشغيل أوامر النظام الخارجية (تم تصحيح المسار الحالي)
        // ═══════════════════════════════════════════════════════
        static void RunCmd(string cmd, string[] args)
        {
            try
            {
                string arguments = string.Join(" ", args);
                string shell, shellArgs;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { shell = "cmd.exe"; shellArgs = $"/c {cmd} {arguments}"; }
                else { shell = "/bin/bash"; shellArgs = $"-c \"{cmd} {arguments}\""; }

                var psi = new ProcessStartInfo(shell, shellArgs)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _cwd // ⬅️ التصحيح السحري: يجعل node و python يشتغلون في المجلد الصحيح
                };
                
                var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine($"\x1b[1;31m{e.Data}\x1b[0m"); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
            }
            catch (Exception ex) { PrintError($"Command Error: {ex.Message}"); }
        }

        static void PrintError(string msg) => Console.WriteLine($"\x1b[1;31m✗ ERROR\x1b[0m {msg}");
        static void PrintSuccess(string msg) => Console.WriteLine($"\x1b[1;32m✓ OK\x1b[0m {msg}");
        static void PrintInfo(string msg) => Console.WriteLine($"\x1b[1;34mℹ INFO\x1b[0m {msg}");
    }
}