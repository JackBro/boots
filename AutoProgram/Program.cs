using CmdLine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoProgram
{
    //Plan
    //Retrieve
        //Execute
        //Retreive

    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var procs = Process.GetProcesses().ToList()
                .Where(p => p.ProcessName.Length > 0)
                .Where(p => p.MainWindowTitle.Length > 0)
                .Select(p => p.ProcessName + "-" + p.MainWindowTitle)
                .ToList();

            //Commands.SyncFiles();
            //return;

            //new HashSet<string>(procs).ToList().ForEach(s => Console.WriteLine(s));

            //This means: we want to run ls in a command prompt
            //var cmd = new AccessCommandPrompt().Perform();
            //new RunCommand("ls", cmd).Perform();

            //This means: we want to run ls, we don't care how
            //new RunCommand("ls").Perform();

            //Specifying the command prompt forces linear execution, while not specifying it may
            //  mean sequential commands are run out of order, and in different prompts


            //var cmd = new AccessCommandPrompt().Perform();
            //new RunCommand("ssh -i ~/.ssh/id_rsa qckbrook@linux.student.cs.uwaterloo.ca", cmd);

            //ssh.Perform();
            //new RunCommand("ls", ssh).Perform();

            //ssh2.Perform();

            //var prompt = Commands.WaterlooStudentPrompt().Perform();

            //var prompt = Commands.UgsterPrompt();

            //new AccessCommandPrompt().Perform();

            /*
            new AccessSSHPrompt(
                        "qckbrook",
                        "linux.student.cs.uwaterloo.ca",
                        promptPrefix: "ubuntu").Perform();
            */

            /*
            var prompt = new UMLLogin().Perform();

            new RunCommand("touch test", prompt).Perform();
            new RunCommand("ls", prompt).Perform();
            */

            //Commands.RunSploit(2);

            //Upload

            //Commands.SyncFiles();

            //Commands.WaterlooStudentPrompt().Perform();

            Commands.RunSploit(4);

            return;

            int sploitNum = 4;
            string sploitFile = "sploit" + sploitNum + ".c";

            var prompt = new UMLLogin().Perform();

            prompt.Run("cd /share");
            prompt.Run("cat > " + sploitFile, dontBlock: true);
            string sploitText = File.ReadAllText(Commands.localDirWin + "/" + sploitFile);
            sploitText
                .Split('\n')
                .Select(line => line.Replace("\r", ""))
                .ToList()
                .ForEach(line =>
                {
                    prompt.Run(line, dontBlock: true, noDelay: true);
                });

            Thread.Sleep(100);
            prompt.Run("\x4");
            prompt.Run("cat " + sploitFile);

            
            //var prompt = new UMLLogin().Perform();
            prompt.Run("cd /share");
            prompt.Run(string.Format("gcc -Wall -ggdb sploit{0}.c -o sploit{0}", sploitNum));
            prompt.Run("./sploit4");


            /*
            prompt.Run("rm gdb.txt");
            prompt.Run("gdb sploit" + sploitNum, dontBlock: true);
            prompt.Run("catch exec", dontBlock: true);
            prompt.Run("run", dontBlock: true);
            Thread.Sleep(200);
            prompt.Run("symbol-file /usr/local/bin/backup", dontBlock: true);
            prompt.Run("y", dontBlock: true);
            prompt.Run("break backup.c:36", dontBlock: true);
            //prompt.Run("break backup.c:39", dontBlock: true);
            prompt.Run("break backup.c:43", dontBlock: true);

            prompt.Run("cont", dontBlock: true);
            Thread.Sleep(500);

            prompt.Run("set logging on", dontBlock: true);

            prompt.Run("info frame", dontBlock: true);
            prompt.Run("print $esp", dontBlock: true);
            prompt.Run("x /512bx (0xffbfdcec) - 400", dontBlock: true);
            prompt.Run("\n", dontBlock: true);
            prompt.Run("\n", dontBlock: true);

            prompt.Run("cont", dontBlock: true);

            prompt.Run("info frame", dontBlock: true);
            prompt.Run("print $esp", dontBlock: true);
            prompt.Run("x /512bx (0xffbfdcec) - 400", dontBlock: true);
            prompt.Run("\n", dontBlock: true);
            prompt.Run("\n", dontBlock: true);

            prompt.Run("quit", dontBlock: true);
            prompt.Run("y", dontBlock: true);


            //Take the gdb.txt file off ugster
            var waterlooPrompt = Commands.WaterlooStudentPrompt().Perform();
            waterlooPrompt.Run(string.Format(
                "rsync -iva qckbrook@ugster05.student.cs.uwaterloo.ca:{0}/gdb.txt ~/gdb.txt",
                Commands.ugsterDir
            ));


            //Take the gdb.txt file off waterloo
            var localPrompt = new AccessCommandPrompt().Perform();
            localPrompt.Run(
string.Format(@"bash
eval `ssh-agent -s`
ssh-add /C/Users/quentin.brooks/.ssh/id_rsa
rsync -iva qckbrook@linux.student.cs.uwaterloo.ca:~/gdb.txt {0}/gdb.txt
exit", Commands.localDir));
            */
        }
    }

    static class Commands
    {
        public static string localDir = @"/C/Users/quentin.brooks/Dropbox/School/2016/CS458/ugster";
        public static string localDirWin = @"C:/Users/quentin.brooks/Dropbox/School/2016/CS458/ugster";
        public static string uwDir = @"~/ugster";
        public static string ugsterDir = @"~/uml/share";

        public static void RunSploit(int sploitNum)
        {
            var umlPrompt = (UMLLogin)new UMLLogin().Perform();
            umlPrompt.Run("cd /share");
            umlPrompt.Run(string.Format("gcc -Wall -ggdb sploit{0}.c -o sploit{0}", sploitNum));
            umlPrompt.Run("./sploit" + sploitNum, beforeBlock: () => umlPrompt.freeTitlePrefix = "AutoShell SUB root@cs458");
            umlPrompt.Run("whoami");
            umlPrompt.Run("exit", beforeBlock: () => umlPrompt.freeTitlePrefix = UMLLogin.basePrefix);
        }

        public static void SyncFiles()
        {
            Directory.EnumerateFiles(localDirWin).ToList().ForEach(filePath =>
            {
                return;
                string text = File.ReadAllText(filePath);
                if(text.Contains("\r\n"))
                {
                    Console.WriteLine("File has windows line endings, changing to unix");
                    text = text.Replace("\r\n", "\n");
                    File.WriteAllText(filePath, text);
                }
            });

            var localPrompt = new AccessCommandPrompt().Perform();

            localPrompt.Run(
string.Format(@"bash
eval `ssh-agent -s`
ssh-add /C/Users/quentin.brooks/.ssh/id_rsa
rsync -iva {0}/ qckbrook@linux.student.cs.uwaterloo.ca:{1}/
exit", localDir, uwDir));


            var waterlooPrompt = Commands.WaterlooStudentPrompt().Perform();

            waterlooPrompt.Run(string.Format(
                "rsync -iva {0}/ qckbrook@ugster05.student.cs.uwaterloo.ca:{1}/",
                uwDir,
                ugsterDir
            ));
        }

        public static AccessSSHPrompt WaterlooStudentPrompt()
        {
            return new AccessSSHPrompt(
                        "qckbrook",
                        "linux.student.cs.uwaterloo.ca",
                        promptPrefix: "ubuntu");
        }

        public static AccessCommandPrompt UgsterPrompt()
        {
            var sshTwice = new NestedOperation<AccessSSHPrompt, AccessCommandPrompt, AccessSSHPrompt, AccessCommandPrompt>(
                    WaterlooStudentPrompt(),
                    new AccessSSHPrompt(
                        "qckbrook",
                        "ugster05.student.cs.uwaterloo.ca",
                        promptPrefix: "qckbrook"),
                    (ssh2, prompt) =>
                    {
                        ssh2.proc = prompt.Handle;
                    }
                );

            return sshTwice.Perform();
        }

        [DllImport("User32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int uMsg, int wParam, uint lParam);
        public const int WM_COMMAND = 0x0111;
        public const int PASTE_IN_COMMAND_PROMPT = 0xfff1;

        public static void Run(this AccessCommandPrompt prompt, string command, bool dontBlock=false, Action beforeBlock = null, bool noDelay=false)
        {
            if (prompt == null)
            {
                prompt = new AccessCommandPrompt();
                prompt.Perform();
            }

            Clipboard.SetText(command + "\r\n");
            SendMessage(prompt.Handle, WM_COMMAND, PASTE_IN_COMMAND_PROMPT, 0);

            if (!noDelay)
            {
                //Wait until the command starts... because I am a bad programmer. Could totally fix this by properly
                //  talking to AutoShell, but you know... not enough time
                Thread.Sleep(100);
            }

            if (!dontBlock)
            {
                if (beforeBlock != null)
                {
                    beforeBlock();
                }

                //Poll until it is done
                while (!prompt.isFreeTitle(prompt.getTitle()))
                {
                    Thread.Sleep(50);
                }
            }
        }


        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GenerateConsoleCtrlEvent(ConsoleCtrlEvent sigevent, int dwProcessGroupId);
        public enum ConsoleCtrlEvent
        {
            CTRL_C = 0,
            CTRL_BREAK = 1,
            CTRL_CLOSE = 2,
            CTRL_LOGOFF = 5,
            CTRL_SHUTDOWN = 6
        }
        public static void SendSignal(this AccessCommandPrompt prompt, ConsoleCtrlEvent signal)
        {
            GenerateConsoleCtrlEvent(signal, ProcessExtensions.GetProcessByHandle(prompt.Handle).Id);
        }
    }

    class UMLLogin : AccessCommandPrompt
    {
        public UMLLogin(bool fresh=false)
        {
            if(fresh)
            {
                //Something that won't match?
                freeTitlePrefix = "A_(S*DJ_*AJSD*AHJSD*(JAS*(_";
            }
        }

        public const string basePrefix = "AutoShell SUB user@cs458-uml";
        public string freeTitlePrefix = basePrefix;
        public override bool isFreeTitle(string otherTitle)
        {
            return otherTitle.StartsWith(freeTitlePrefix);
        }

        public override void Execute()
        {
            proc = Commands.UgsterPrompt().Handle;

            freeTitlePrefix = "AutoShell SUB cs458-uml login:";
            this.Run("uml");
            freeTitlePrefix = basePrefix;
            this.Run("user");
        }
    }

    //For when operation 2 depends on operation 1... hmm... not sure if I like this
    class NestedOperation<Op1, T1, Op2, T2> : Operation<T2>
        where Op1 : Operation<T1>
        where Op2 : Operation<T2>
    {
        Op1 op1;
        Op2 op2;
        Action<Op2, T1> useOp1Result;
        public NestedOperation(Op1 op1, Op2 op2, Action<Op2, T1> useOp1Result)
        {
            this.op1 = op1;
            this.op2 = op2;
            this.useOp1Result = useOp1Result;
        }

        public override bool Retrieve(ref T2 result)
        {
            return op2.Retrieve(ref result);
        }

        public override void Execute()
        {
            T1 op1result = op1.Perform();
            useOp1Result(op2, op1result);
            op2.Perform();
        }
    }

    class AccessSSHPrompt : AccessCommandPrompt
    {
        string command;
        public string promptPrefix;
        public AccessSSHPrompt(
            string username, 
            string server, 
            string identity = "/C/Users/quentin.brooks/.ssh/id_rsa", 
            string promptPrefix = null,
            IntPtr? proc = null)
        {
            //-t -t is for http://stackoverflow.com/questions/7114990/pseudo-terminal-will-not-be-allocated-because-stdin-is-not-a-terminal
            //  because we get errors otherwise
            command = string.Format("ssh -t -t -i {0} {1}@{2}", identity, username, server);
            this.promptPrefix = promptPrefix;
            if (proc.HasValue)
            {
                this.proc = proc.Value;
            }
        }

        string prevPrompt = null;
        public override bool isFreeTitle(string otherTitle)
        {
            if (promptPrefix == null && prevPrompt == null) return false;
            if (promptPrefix == null)
            {
                //Case of nested ssh shells
                if (prevPrompt.StartsWith("AutoShell SUB") && otherTitle.StartsWith(prevPrompt))
                {
                    return false;
                }

                return otherTitle.StartsWith("AutoShell SUB");
            }

            return otherTitle.StartsWith(freeTitle + " SUB " + promptPrefix);
        }

        public override void Execute()
        {
            if(proc == IntPtr.Zero)
            {
                var cmd = new AccessCommandPrompt();
                cmd.Perform();

                //Take the proc for ourselves... we can't just call base.Execute, as we have already cannabalized
                //  our version of AccessCommandPrompt by interfering with freeTitle.
                this.proc = cmd.Handle;
            }

            prevPrompt = this.getTitle();

            this.Run(command);

            string promptPrefix = this.getTitle().Substring("AutoShell SUB ".Length);
            int index = promptPrefix.IndexOf(":");
            if(index < 0)
            {
                throw new Exception("Do not know how to identify this SSH prompt, I should write code for this case so it doesn't have to crash (although it will always be less useful to not identify when the shell is reading for input");
            }
            promptPrefix = promptPrefix.Substring(0, index);
        }
    }

    //An operation is less something we do, and more something we insure. We may open the patient
    //  up and see that everything is fine, and then do nothing (except retrive proof everything
    //  is alright).
    //EX: When running commands we less want to have them run, as have them in a running state
    abstract class Operation<T>
    {
        volatile bool calledPerform = false;
        [DebuggerStepThrough]
        public T Perform()
        {
            if (calledPerform)
            {
                //throw new Exception("Do not call Perform on an operation twice, it should have no effect (and if it does the Operation is misbehaving).");
            }
            calledPerform = true;
            

            T result = default(T);
            bool applied = Retrieve(ref result);
            if(!applied)
            {
                Execute();
                if (!Retrieve(ref result))
                {
                    throw new Exception("Execute ran but Check did not state operation was applied for " + this.ToString());
                }
            }
            return result;
        }

        //Returns true if operation is applied, and sets val with the result
        public abstract bool Retrieve(ref T result);

        //Applies operation
        public abstract void Execute();
    }

    abstract class VoidOperation : Operation<bool>
    {
        bool executed = false;

        [DebuggerStepThrough]
        public sealed override bool Retrieve(ref bool result)
        {
            result = true;
            return executed;
        }
        [DebuggerStepThrough]
        public sealed override void Execute()
        {
            executed = true;
            ExecuteVoid();
        }

        abstract protected void ExecuteVoid();
    }

    public static class ProcessExtensions
    {
        public static Process GetProcessByHandle(IntPtr handle, string processName = null)
        {
            Process[] procs = processName == null 
                ? Process.GetProcesses()
                : Process.GetProcessesByName(processName);
            return procs.Where(
                p => p.MainWindowHandle == handle 
                //|| p.Handle == handle
            ).FirstOrDefault();
        }

        public static List<Process> GetParentProcesses(IntPtr handle)
        {
            List<Process> procs = new List<Process>();

            Process parent = ParentProcessUtilities.GetParentProcess(handle);
            while(parent != null)
            {
                procs.Add(parent);
                parent = ParentProcessUtilities.GetParentProcess(parent.Handle);
            }

            return procs;
        }
    }

    //http://stackoverflow.com/questions/394816/how-to-get-parent-process-in-net-in-managed-way
    /// <summary>
    /// A utility class to determine a process parent.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ParentProcessUtilities
    {
        // These members must match PROCESS_BASIC_INFORMATION
        internal IntPtr Reserved1;
        internal IntPtr PebBaseAddress;
        internal IntPtr Reserved2_0;
        internal IntPtr Reserved2_1;
        internal IntPtr UniqueProcessId;
        internal IntPtr InheritedFromUniqueProcessId;

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ParentProcessUtilities processInformation, int processInformationLength, out int returnLength);

        /// <summary>
        /// Gets the parent process of the current process.
        /// </summary>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess()
        {
            return GetParentProcess(Process.GetCurrentProcess().Handle);
        }

        /// <summary>
        /// Gets the parent process of specified process.
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(int id)
        {
            Process process = Process.GetProcessById(id);
            return GetParentProcess(process.Handle);
        }

        /// <summary>
        /// Gets the parent process of a specified process.
        /// </summary>
        /// <param name="handle">The process handle.</param>
        /// <returns>An instance of the Process class or null if an error occurred.</returns>
        public static Process GetParentProcess(IntPtr handle)
        {
            ParentProcessUtilities pbi = new ParentProcessUtilities();
            int returnLength;
            int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
            if (status != 0)
                return null;

            try
            {
                return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
            }
            catch (ArgumentException)
            {
                // not found
                return null;
            }
        }
    }

    class AccessCommandPrompt : Operation<AccessCommandPrompt>
    {
        public string freeTitle = @"AutoShell";
        public string shellExe = @"C:\Users\quentin.brooks\Dropbox\boots\AutoShell\bin\Debug\AutoShell.exe";
        public string procName = "AutoShell";

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
        const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public IntPtr Handle { get { return proc; } }

        public IntPtr proc = IntPtr.Zero;
        private void setProc(IntPtr proc)
        {
            this.proc = proc;

            IntPtr prevWindow = GetForegroundWindow();
            Process prevProc = ProcessExtensions.GetProcessByHandle(prevWindow);
            List<Process> parents = ProcessExtensions.GetParentProcesses(prevProc.Handle);
            parents.Insert(0, prevProc);

            /* //Eh... skip this for a bit
            SetForegroundWindow(proc);
            GetForegroundWindow();

            for (int ix = 0; ix < parents.Count; ix++)
            {
                Process parent = ProcessExtensions.GetProcessByHandle(parents[ix].MainWindowHandle);
                if(parent != null)
                {
                    string title = parent.MainWindowTitle;
                    SetForegroundWindow(parent.MainWindowHandle);
                    IntPtr newFocus = GetForegroundWindow();
                    if(newFocus == parent.MainWindowHandle)
                    {
                        break;
                    }
                }
            }
            */
        }

        public string getTitle()
        {
            return ProcessExtensions.GetProcessByHandle(Handle, procName).MainWindowTitle;
        }

        public virtual bool isFreeTitle(string otherTitle)
        {
            //Replace spaces with nothing... because I have observed random spaces, and idk what to do about them
            return freeTitle.Replace(" ", "").ToLower() == otherTitle.Replace(" ", "").ToLower();
        }

        public override bool Retrieve(ref AccessCommandPrompt prompt)
        {
            //Find free cmd.exe
            List<Process> procs = Process.GetProcessesByName(procName).ToList();

            procs = procs.Where(p => isFreeTitle(p.MainWindowTitle)).ToList();

            if(procs.Count == 0)
            {
                return false;
            }

            //TODO: We could do some more verification here to insure it is valid... perhaps
            //  there should be a Function passed into this class to validate processes.
            setProc(procs.First().MainWindowHandle);

            prompt = this;
            return true;
        }

        public override void Execute()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Normal;
            startInfo.FileName = shellExe;

            Process process = Process.Start(startInfo);
            while(process.MainWindowHandle == IntPtr.Zero)
            {
                Thread.Sleep(50);
            }
            setProc(process.MainWindowHandle);
            if(proc == IntPtr.Zero)
            {
                throw new Exception("wtf");
            }
        }
    }
}