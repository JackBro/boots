using CmdLine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
        static string dir = @"C:\Users\quentin.brooks\Dropbox\boots\test";
        [STAThread]
        static void Main(string[] args)
        {
            var procs = Process.GetProcesses().ToList()
                .Where(p => p.ProcessName.Length > 0)
                .Where(p => p.MainWindowTitle.Length > 0)
                .Select(p => p.ProcessName + "-" + p.MainWindowTitle)
                .ToList();

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

            /*
            new AccessSSHPrompt(
                        "qckbrook",
                        "linux.student.cs.uwaterloo.ca",
                        promptPrefix: "ubuntu").Perform();
            */

            
            var prompt = new UMLLogin().Perform();

            new RunCommand("touch test", prompt).Perform();
            new RunCommand("ls", prompt).Perform();
            
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

        const string basePrefix = "AutoShell SUB user@cs458-uml";
        string freeTitlePrefix = basePrefix;
        public override bool isFreeTitle(string otherTitle)
        {
            return otherTitle.StartsWith(freeTitlePrefix);
        }

        public override void Execute()
        {
            {
                var sshTwice = new NestedOperation<AccessSSHPrompt, AccessCommandPrompt, AccessSSHPrompt, AccessCommandPrompt>(
                    new AccessSSHPrompt(
                        "qckbrook",
                        "linux.student.cs.uwaterloo.ca",
                        promptPrefix: "ubuntu"),
                    new AccessSSHPrompt(
                        "qckbrook",
                        "ugster05.student.cs.uwaterloo.ca",
                        promptPrefix: "qckbrook"),
                    (ssh2, prompt) =>
                    {
                        ssh2.proc = prompt.Handle;
                    }
                );

                var ssh = sshTwice.Perform();
                this.proc = ssh.Handle;
            }

            freeTitlePrefix = "AutoShell SUB cs458-uml login:";
            new RunCommand("uml", this).Perform();
            freeTitlePrefix = basePrefix;
            new RunCommand("user", this).Perform();
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
        string promptPrefix;
        public AccessSSHPrompt(
            string username, 
            string server, 
            string identity = "~/.ssh/id_rsa", 
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

            new RunCommand(command, this).Perform();

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

    class RunCommand : VoidOperation
    {
        [DllImport("User32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int uMsg, int wParam, uint lParam);
        public const int WM_COMMAND = 0x0111;
        public const int PASTE_IN_COMMAND_PROMPT = 0xfff1;
        
        string command;
        AccessCommandPrompt prompt;
        bool dontBlock;
        public RunCommand(
            string command, 
            AccessCommandPrompt prompt = null, 
            bool dontBlock = false) //Makes our execute return right away, before the command finishes
        {
            this.prompt = prompt;
            this.command = command;
            this.dontBlock = dontBlock;
        }

        protected override void ExecuteVoid()
        {
            if(prompt == null)
            {
                prompt = new AccessCommandPrompt();
                prompt.Perform();
            }

            Clipboard.SetText(command + "\r\n");
            SendMessage(prompt.Handle, WM_COMMAND, PASTE_IN_COMMAND_PROMPT, 0);

            if(!this.dontBlock)
            {
                //Poll until it is done
                while (!prompt.isFreeTitle(prompt.getTitle()))
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}