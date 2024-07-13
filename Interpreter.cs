using System.Net;
using System;
using Renci.SshNet;
using System.Threading;
using Renci.SshNet.Sftp;
using System.Diagnostics;

namespace CommandRetriever
{
    public class Credentials
    {
        public string username;
        public string password;
        public string host;
    }

    internal class Program
    {
        static System.Threading.Thread _commandThread;
        static System.Boolean commandExecuterRunning = true;
        static System.Boolean logCleanerRunning = true;
        static System.Threading.Thread _logClearThread;

        static void Main(string[] args)
        {
            Logger("Program starting!");
            _commandThread = new System.Threading.Thread(() => CommandRunner());
            _commandThread.Start();

            Console.CancelKeyPress += Console_CancelKeyPress;

        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            commandExecuterRunning = false;
            
            int attempts = 20;

            while (attempts > 0 && _logClearThread.IsAlive && logCleanerRunning)
            {
                attempts--;
                Logger("Waiting on log cleaner to exit. Will force close in " + attempts + " seconds if necessary");
                Thread.Sleep(1000);
            }

            _logClearThread.Abort();

            attempts = 10;

            while (attempts > 0 && _commandThread.IsAlive)
            {
                attempts--;
                Logger("Waiting on commands to complete before exiting. Will force close in " + attempts + " seconds if necessary");
                Thread.Sleep(1000);
            }

            Logger("Program exiting");
            System.Environment.Exit(0);
        }

        static void LogCleaner()
        {
            while (true)
            {
                logCleanerRunning = true;

                logCleanerRunning = false;
            }
        }

        static void Logger(System.String log)
        {
            Logger(log);

            using (StreamWriter sw = new StreamWriter(System.DateTime.Now.Date + "log.txt"))
            {
                sw.WriteLine(System.DateTime.Now + " " + log);
            }
        }

        static void CommandRunner()
        {
            System.Boolean running = true;
            Credentials credentials = new Credentials();
            Logger("Please enter your username");
            credentials.username = Console.ReadLine();
            Logger("Please enter your password");
            credentials.password = Console.ReadLine();
            Logger("Please enter the FTPS host");
            credentials.host = Console.ReadLine();

            while (running)
            {
                Thread.Sleep(10000);

                using (SftpClient client = new SftpClient(credentials.host, credentials.username, credentials.password))
                {
                    try
                    {
                        client.Connect();
                        Logger("Retrieving commands.");

                        IEnumerable<SftpFile> files = client.ListDirectory("/");

                        if (files.Count() == 0)
                        {
                            Logger("No commands to execute");
                            continue;
                        }

                        if (files.Count() > 4)
                        {
                            Logger("There are more than 5 pending commands. Do you want to delete them and shutdown? (y)es or (n)o");
                            String userInput = Console.ReadLine();

                            while (userInput != "y" && userInput != "n")
                            {
                                Logger("Please select y or n");
                                userInput = Console.ReadLine();
                            }

                            if (userInput == "y")
                            {
                                foreach (SftpFile file in files)
                                {
                                    client.Delete(file.FullName);
                                }
                                System.Environment.Exit(0);
                            }
                        }

                        System.DateTime issueDate = System.DateTime.MaxValue;
                        foreach (SftpFile file in files)
                        {
                            if (file.LastWriteTimeUtc < issueDate)
                            {
                                issueDate = file.LastWriteTimeUtc;
                                SftpFile execFile = file;
                            }

                            Stream fileContent = File.OpenRead(file.FullName);
                            client.DownloadFile(file.FullName, fileContent);

                            System.String fileContentString = "";
                            using (StreamReader sr = new StreamReader(fileContent))
                            {
                                fileContentString = sr.ReadToEnd();
                            }

                            if (!fileContentString.Contains("Execute:") ||
                                !fileContentString.Contains("Username:") ||
                                !fileContentString.Contains("Password:"))
                            {
                                Logger("The command in " + file.FullName + " is malformed. ");
                                Logger("It contains the text " + fileContentString);
                                Logger("Deleting the file");
                                client.DeleteFile(file.FullName);
                                continue;
                            }

                            System.String fileUser = fileContentString.Substring(fileContentString.IndexOf("UserName: ") + 10,
                                fileContentString.IndexOf("Password: "));
                            System.String filePass = fileContentString.Substring(fileContentString.IndexOf("Password: ") + 10,
                                fileContentString.IndexOf("Execute: "));

                            if (fileUser != credentials.username
                                ||
                                filePass != credentials.password)
                            {
                                Logger("Command not issued to this user found. Deleting");
                                client.DeleteFile(file.FullName);
                                continue;
                            }


                            else
                            {

                                System.String commandText = fileContentString.Substring(fileContentString.IndexOf("Execute: ") + 9);

                                try
                                {
                                    Logger("Issuing the command " + commandText);
                                    System.String command = commandText.Substring(0, commandText.IndexOf(" "));
                                    System.String arguments = commandText.Substring(commandText.IndexOf(" ") + 1);
                                    Process p = new Process();
                                    p.StartInfo.FileName = command;
                                    p.StartInfo.Arguments = arguments;
                                    p.StartInfo.UseShellExecute = false;
                                    p.StartInfo.CreateNoWindow = false;
                                    p.Start();
                                }
                                catch (Exception e)
                                {
                                    Logger("Could not execute command" + commandText);
                                    Logger(e.Message);
                                    Logger("Do you want to delete this command? (y)es or (n)o");
                                    string userInput = Console.ReadLine();

                                    while (userInput != "y" && userInput != "n")
                                    {
                                        Logger("Please select y or n");
                                        userInput = Console.ReadLine();
                                    }

                                    if (userInput == "y")
                                    {
                                        client.DeleteFile(file.FullName);
                                    }
                                }

                            }
                        }

                    }
                    catch (Exception e)
                    {
                        Logger("Exception " + e.Message);
                    }
                    finally
                    {
                        client.Disconnect();
                    }
                }
            }
        }
    }
}

