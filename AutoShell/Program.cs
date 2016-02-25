using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoShell
{
    class Program
    {
        const string RootTitle = "AutoShell";

        //This is the amount of time we wait until we are sure we are at a prompt. Should be longer
        //  (if a user is looking at a > for a few seconds, they will think it is a prompt, they
        //  have no way of knowing it isn't), but we want to avoid the user typing at a prompt
        //  before we identify it as such, to make the logic work because I am not a good programmer.
        volatile static int atPromptTimeout = 250;
        volatile static bool atPrompt = false;
        volatile static bool atPromptLine = false;
        volatile static bool inSubPrompt = false;

        const int CTRL_C_EVENT = 0;
        const int CTRL_BREAK_EVENT = 1;

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

        static void Main(string[] args)
        {
            

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "cmd.exe";
            //start.Arguments = "/c ls";
            start.RedirectStandardError = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.UseShellExecute = false;

            Process process = Process.Start(start);
            StreamReader stdOut = process.StandardOutput;
            StreamWriter stdIn = process.StandardInput;
            StreamReader stdError = process.StandardError;

            object reading = new object();

            string lastLine = "";
            string promptLocation = "";

            Console.Title = RootTitle;
            //Should like... use classes for this thing
            Timer atPromptTimer = new Timer((ev) =>
            {
                string test = process.MainWindowTitle;

                atPrompt = true;

                if(!atPromptLine)
                {
                    promptLocation = lastLine;
                    if(!promptLocation.Contains("\\"))
                    {
                        //Hmm... hack... but lets assume it means we are in a sub prompt... we should find a better solution
                        inSubPrompt = true;
                    }
                    else
                    {
                        inSubPrompt = false;
                    }

                    lastLine = "";
                }
                atPromptLine = true;

                string title = RootTitle;
                if(inSubPrompt)
                {
                    title += " SUB " + promptLocation;
                }
                Console.Title = title;
            });

            HashSet<char> promptStarts = new HashSet<char> { '>', '$', ':', '#' };
            char lastChar = ' ';
            Thread readOutputThread = new Thread(() =>
            {
                while (true)
                {
                    int ch = stdOut.Read();
                    lock (reading)
                    {
                        while (true)
                        {
                            //Hmm... there is sometimes " >"? AND "$ "

                            if (promptStarts.Contains((char)ch)
                            || promptStarts.Contains(lastChar) && (char)ch == ' ')
                            {
                                atPromptTimer.Change(atPromptTimeout, Timeout.Infinite);
                            }
                            else
                            {
                                atPromptTimer.Change(Timeout.Infinite, Timeout.Infinite);
                            }
                            atPrompt = false;
                            if((char)ch == '\n')
                            {
                                if (atPromptLine)
                                {
                                    Console.Title = RootTitle + " - " + lastLine;
                                    atPromptLine = false;
                                }
                                lastLine = "";
                            }
                            else
                            {
                                lastLine += (char)ch;
                            }

                            lastChar = (char)ch;
                            Console.Write((char)ch);

                            int chPeek = stdOut.Peek();
                            if (chPeek == -1) break;
                            ch = stdOut.Read();
                        }
                    }
                }
            });
            readOutputThread.Start();


            Thread readErrorThread = new Thread(() =>
            {
                while (true)
                {
                    int ch = stdError.Read();

                    lock(reading)
                    {
                        while (true)
                        {
                            using (new SetConsoleColor(ConsoleColor.Red))
                            {
                                Console.Write((char)ch);
                            }

                            int chPeek = stdError.Peek();
                            if (chPeek == -1) break;
                            ch = stdError.Read();
                        }
                    }
                }
            });
            readErrorThread.Start();


            Thread readInputThread = new Thread(() =>
            {
                while (true)
                {
                    int ch = Console.Read();
                    //Don't print \r
                    if (ch == 13) continue;
                    if (ch != -1)
                    {
                        stdIn.Write((char)ch);
                    }

                    if(ch == -1)
                    {
                        if (inSubPrompt)
                        {
                            stdIn.Write('\x3');
                        }
                        else
                        {
                            //Hmm... I think this is actually unnecessary,
                            //  I think it gets it anyway because it is part of our process group?
                            //GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C, process.Id);
                        }
                    }
                }
            });
            readInputThread.Start();


            Console.CancelKeyPress += (obj, ev) => {
                ev.Cancel = true;
            };

            readOutputThread.Join();

            //process.sta
        }
    }

    class SetConsoleColor : IDisposable
    {
        ConsoleColor prevColor;
        public SetConsoleColor(ConsoleColor color)
        {
            this.prevColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
        }

        public void Dispose()
        {
            Console.ForegroundColor = prevColor;
        }
    }
}
