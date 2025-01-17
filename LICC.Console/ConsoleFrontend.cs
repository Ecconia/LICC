﻿using LICC.API;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TrueColorConsole;
using SConsole = System.Console;

namespace LICC.Console
{
    public class ConsoleFrontend : Frontend, ILineReader
    {
        private (int X, int Y) StartPos;
        private string Buffer;
        private int CursorPos;

        private readonly Queue<ConsoleKeyInfo> QueuedKeys = new Queue<ConsoleKeyInfo>();
        private readonly ConsoleOptions Options;

        private bool VTModeEnabled;
        private bool IsInputPaused;
        private Thread ReaderThread;

        private bool IsVTConsoleEnabled => VTModeEnabled && VTConsole.IsEnabled;

        public override CColor DefaultForeground => SConsole.ForegroundColor;

        public ConsoleFrontend(bool enableVTMode, ConsoleOptions options = null)
        {
            this.VTModeEnabled = enableVTMode && IsWindows() && VTConsole.IsSupported;
            this.Options = options ?? ConsoleOptions.Default;
        }

        private static bool IsWindows()
        {
            string windir = Environment.GetEnvironmentVariable("windir");
            return !string.IsNullOrEmpty(windir) && windir.Contains(@"\") && Directory.Exists(windir);
        }

        protected override void Init()
        {
            SConsole.TreatControlCAsInput = true;

            if (VTModeEnabled)
                VTModeEnabled = VTConsole.Enable();

            Buffer = "";
            StartPos = (SConsole.CursorLeft, SConsole.CursorTop);
            RewriteBuffer("");
        }

        /// <summary>
        /// Blocks the current thread and begins reading input.
        /// </summary>
        public void BeginRead()
        {
            ReaderThread = new Thread(() =>
            {
                while (true)
                {
                    var key = SConsole.ReadKey(true);
                    bool ctrl = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;

                    if (VTModeEnabled)
                    {
                        if (key.KeyChar == 27)
                        {
                            string keySeq = "" + SConsole.ReadKey(true).KeyChar + SConsole.ReadKey(true).KeyChar;
                            ConsoleKey? newKey = null;

                            if (keySeq == "[A")
                                newKey = ConsoleKey.UpArrow;
                            else if (keySeq == "[B")
                                newKey = ConsoleKey.DownArrow;
                            else if (keySeq == "[C")
                                newKey = ConsoleKey.RightArrow;
                            else if (keySeq == "[D")
                                newKey = ConsoleKey.LeftArrow;
                            else if (keySeq == "[3" && SConsole.ReadKey(true).KeyChar == '~')
                                newKey = ConsoleKey.Delete;

                            if (newKey != null)
                                key = new ConsoleKeyInfo('\0', newKey.Value, false, false, ctrl);
                            else
                                SConsole.Beep();
                        }
                        else if (key.KeyChar == 3)
                        {
                            key = new ConsoleKeyInfo('c', ConsoleKey.C, false, false, true);
                        }
                        else if (key.KeyChar == 127)
                        {
                            key = new ConsoleKeyInfo('\0', ConsoleKey.Backspace, false, false, false);
                        }
                        else if (key.KeyChar == '\b')
                        {
                            key = new ConsoleKeyInfo('\0', ConsoleKey.Backspace, false, false, true);
                        }
                    }

                    if (IsInputPaused)
                        QueuedKeys.Enqueue(key);
                    else
                        HandleKey(key);
                }
            })
            {
                Name = "LICC Reader thread",
                IsBackground = true
            };
            ReaderThread.Start();
            ReaderThread.Join();
        }

        private void HandleKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Backspace:
                    if (Buffer.Length > 0 && CursorPos > 0)
                    {
                        int charsToDelete = 1;

                        if ((key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
                        {
                            int spaceIndex = Buffer.LastIndexOf(' ', CursorPos - 1);

                            charsToDelete = spaceIndex != -1 ? Buffer.Length - spaceIndex : CursorPos;
                        }

                        Buffer = Buffer.Remove(CursorPos - charsToDelete, charsToDelete);

                        SConsole.MoveBufferArea(SConsole.CursorLeft, SConsole.CursorTop, SConsole.BufferWidth - SConsole.CursorLeft, 1, SConsole.CursorLeft - charsToDelete, SConsole.CursorTop);
                        SConsole.CursorLeft -= charsToDelete;
                        CursorPos -= charsToDelete;
                    }
                    break;

                case ConsoleKey.Delete:
                    if (Buffer.Length > 0 && CursorPos < Buffer.Length)
                    {
                        int charsToDelete = 1;

                        if ((key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
                        {
                            int spaceIndex = Buffer.IndexOf(' ', CursorPos);

                            charsToDelete = spaceIndex != -1 ? spaceIndex - CursorPos : Buffer.Length - CursorPos;
                        }

                        Buffer = Buffer.Remove(CursorPos, charsToDelete);
                        SConsole.MoveBufferArea(SConsole.CursorLeft + charsToDelete, SConsole.CursorTop, SConsole.BufferWidth - SConsole.CursorLeft - charsToDelete, 1, SConsole.CursorLeft, SConsole.CursorTop);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (CursorPos > 0)
                    {
                        int charsToMove = 1;

                        if ((key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
                        {
                            int spaceIndex = Buffer.LastIndexOf(' ', CursorPos - 2);

                            charsToMove = spaceIndex != -1 ? CursorPos - spaceIndex - 1 : CursorPos;
                        }

                        CursorPos -= charsToMove;
                        SConsole.CursorLeft -= charsToMove;
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if (CursorPos < Buffer.Length)
                    {
                        int charsToMove = 1;

                        if ((key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
                        {
                            int spaceIndex = Buffer.IndexOf(' ', CursorPos + 1);

                            charsToMove = spaceIndex != -1 ? spaceIndex - CursorPos : Buffer.Length - CursorPos;
                        }

                        CursorPos += charsToMove;
                        SConsole.CursorLeft += charsToMove;
                    }
                    break;

                case ConsoleKey.UpArrow:
                {
                    string histItem = History.GetPrevious();

                    if (histItem != null)
                        RewriteBuffer(histItem);
                    break;
                }

                case ConsoleKey.DownArrow:
                {
                    string histItem = History.GetNext();

                    if (histItem != null)
                        RewriteBuffer(histItem);
                    break;
                }

                case ConsoleKey.Enter:
                case ConsoleKey.C when (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control:
                    SConsole.WriteLine();
                    StartPos = (SConsole.CursorLeft, SConsole.CursorTop);

                    string line = Buffer;
                    RewriteBuffer("");

                    if (key.Key == ConsoleKey.Enter)
                        OnLineInput(line);

                    break;

                default:
                    if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        SConsole.MoveBufferArea(SConsole.CursorLeft, SConsole.CursorTop, SConsole.BufferWidth - 1 - SConsole.CursorLeft, 1, SConsole.CursorLeft + 1, SConsole.CursorTop);

                        Write(key.KeyChar.ToString(), Buffer.IndexOf(' ', 0, CursorPos) == -1 ? ConsoleColor.Yellow : ConsoleColor.Cyan);

                        Buffer = Buffer.Insert(CursorPos, key.KeyChar.ToString());

                        CursorPos++;
                    }
                    break;
            }
        }

        private void RewriteBuffer(string newStr)
        {
            string prevBuffer = Buffer;
            Buffer = newStr;

            int spaceIndex = newStr.IndexOf(' ');
            string cmdName = spaceIndex == -1 ? newStr : newStr.Substring(0, spaceIndex);
            string rest = spaceIndex == -1 ? "" : newStr.Substring(spaceIndex);

            SConsole.SetCursorPosition(StartPos.X, StartPos.Y);
            Write("> ", ConsoleColor.DarkYellow);
            Write(cmdName, ConsoleColor.Yellow);
            Write(rest, ConsoleColor.Cyan);

            if (newStr.Length < prevBuffer.Length)
                SConsole.Write(new string(' ', prevBuffer.Length - newStr.Length));

            CursorPos = newStr.Length;
            SConsole.SetCursorPosition(StartPos.X + CursorPos + 2, StartPos.Y);
        }

        protected override void PauseInput()
        {
            IsInputPaused = true;

            SConsole.SetCursorPosition(StartPos.X, StartPos.Y);
            SConsole.Write(new string(' ', Buffer.Length + 2));
            SConsole.SetCursorPosition(StartPos.X, StartPos.Y);
        }

        protected override void ResumeInput()
        {
            IsInputPaused = false;

            StartPos = (SConsole.CursorLeft, SConsole.CursorTop);
            RewriteBuffer(Buffer);

            while (QueuedKeys.Count > 0)
                HandleKey(QueuedKeys.Dequeue());
        }

        public override void WriteLine(string str)
        {
            SConsole.WriteLine(str);
        }

        public override void Write(string str, CColor color)
        {
            if (IsVTConsoleEnabled)
            {
                if (Options.UseColoredOutput)
                {
                    VTConsole.Write(str, Color.FromArgb(color.R, color.G, color.B));

                    var c = CColor.FromConsoleColor(ConsoleColor.Gray);
                    VTConsole.SetColorForeground(Color.FromArgb(c.R, c.G, c.B));
                }
                else
                {
                    VTConsole.Write(str);
                }
            }
            else
            {
                if (Options.UseColoredOutput)
                {
                    var prev = SConsole.ForegroundColor;
                    SConsole.ForegroundColor = color.ToConsoleColor();
                    SConsole.Write(str);
                    SConsole.ForegroundColor = prev;
                }
                else
                {
                    SConsole.Write(str);
                }
            }
        }

        protected override void Stop()
        {
            ReaderThread.Abort();
        }

        /// <summary>
        /// Fires up a console with the default settings.
        /// </summary>
        public static void StartDefault(out CommandConsole console, string fileSystemRoot = null, bool enableVtMode = true)
        {
            var frontend = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? (Frontend)new ConsoleFrontend(enableVtMode)
                : new PlainTextConsoleFrontend();
            console = fileSystemRoot == null ? new CommandConsole(frontend) : new CommandConsole(frontend, fileSystemRoot);
            console.Commands.RegisterCommandsInAllAssemblies();

            console.RunAutoexec();

            (frontend as ILineReader).BeginRead();
        }

        /// <summary>
        /// Fires up a console with the default settings.
        /// </summary>
        public static void StartDefault(string fileSystemRoot = null, bool enableVtMode = true)
            => StartDefault(out _, fileSystemRoot, enableVtMode);
    }
}
