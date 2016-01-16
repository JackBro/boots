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
            //This means: we want to run ls in a command prompt
            IntPtr cmd = new AccessCommandPrompt().Perform();
            new RunCommand("ls", cmd).Perform();

            //This means: we want to run ls, we don't care how
            //new RunCommand("ls").Perform();

            //Specifying the command prompt forces linear execution, while not specifying it may
            //  mean sequential commands are run out of order, and in different prompts
        }
    }
    
    abstract class Operation<T>
    {
        volatile bool calledPerform = false;
        public T Perform()
        {
            if (calledPerform)
            {
                throw new Exception("Do not call Perform on an operation twice, it should have no effect (and if it does the Operation is misbehaving).");
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
        abstract protected bool Retrieve(ref T result);

        //Applies operation
        abstract protected void Execute();
    }

    abstract class VoidOperation : Operation<bool>
    {
        bool executed = false;

        protected sealed override bool Retrieve(ref bool result)
        {
            result = true;
            return executed;
        }
        protected sealed override void Execute()
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

    class AccessCommandPrompt : Operation<IntPtr>
    {
        public const string freeTitle = @"C:\Windows\System32\cmd.exe";
        public const string procName = "cmd";

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
        const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();


        IntPtr proc = IntPtr.Zero;
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

        protected override bool Retrieve(ref IntPtr procOut)
        {
            //Find free cmd.exe

            List<Process> procs = Process.GetProcessesByName(procName).ToList();

            procs = procs.Where(p => p.MainWindowTitle == freeTitle).ToList();

            if(procs.Count == 0)
            {
                return false;
            }

            //TODO: We could do some more verification here to insure it is valid... perhaps
            //  there should be a Function passed into this class to validate processes.
            setProc(procs.First().MainWindowHandle);

            procOut = proc;
            return true;
        }

        protected override void Execute()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Normal;
            startInfo.FileName = "cmd.exe";

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
        IntPtr? handle;
        bool dontBlock;
        public RunCommand(
            string command, 
            IntPtr? handle = null, 
            bool dontBlock = false) //Makes our execute return right away, before the command finishes
        {
            this.handle = handle;
            this.command = command;
            this.dontBlock = dontBlock;
        }

        protected override void ExecuteVoid()
        {
            if(!handle.HasValue)
            {
                handle = new AccessCommandPrompt().Perform();
            }

            Clipboard.SetText(command + "\r\n");
            SendMessage(handle.Value, WM_COMMAND, PASTE_IN_COMMAND_PROMPT, 0);

            if(!this.dontBlock)
            {
                Func<string> getTitle = () => ProcessExtensions.GetProcessByHandle(handle.Value, AccessCommandPrompt.procName).MainWindowTitle;
                
                //Poll until it is done
                while (getTitle() != AccessCommandPrompt.freeTitle)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}