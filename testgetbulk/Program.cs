﻿/*
 * Created by SharpDevelop.
 * User: lextm
 * Date: 2008/5/11
 * Time: 12:25
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Mono.Options;
using Lextm.SharpSnmpLib.Security;
using Lextm.SharpSnmpLib.Messaging;

namespace TestGetBulk
{
    class TestGetBulk
    {
        static void Main(string[] args)
        {
            string community = "public";
            bool show_help   = false;
            bool show_version = false;
            VersionCode version = VersionCode.V1;
            int timeout = 1000;
            int retry = 0;
            SecurityLevel level = SecurityLevel.None | SecurityLevel.Reportable;
            string user = string.Empty;
            string authentication = string.Empty;
            string authPhrase = string.Empty;
            string privacy = string.Empty;
            string privPhrase = string.Empty;
            int maxRepetitions = 10;
            int nonRepeaters = 0;

            OptionSet p = new OptionSet()
                .Add("c:", "-c for community name, (default is public)", delegate (string v) { if (v != null) community = v; })
                .Add("l:", "-l for security level, (default is noAuthNoPriv)", delegate(string v)
                     {
                         if (v == "noAuthNoPriv")
                         {
                             level = SecurityLevel.None | SecurityLevel.Reportable;
                         }
                         else if (v == "authNoPriv")
                         {
                             level = SecurityLevel.Authentication | SecurityLevel.Reportable;
                         }
                         else if (v == "authPriv")
                         {
                             level = SecurityLevel.Authentication | SecurityLevel.Privacy | SecurityLevel.Reportable;
                         }
                     })
                .Add("Cn:", "-Cn for non-repeaters (default is 0)", delegate(string v) { nonRepeaters = int.Parse(v); })
                .Add("Cr:", "-Cr for max-repetitions (default is 10)", delegate(string v) { maxRepetitions = int.Parse(v); })
                .Add("a:", "-a for authentication method", delegate(string v) { authentication = v; })
                .Add("A:", "-A for authentication passphrase", delegate(string v) { authPhrase = v; })
                .Add("x:", "-x for privacy method", delegate(string v) { privacy = v; })
                .Add("X:", "-X for privacy passphrase", delegate(string v) { privPhrase = v; })
                .Add("u:", "-u for security name", delegate(string v) { user = v; })
                .Add("h|?|help", "-h, -?, -help for help.", delegate(string v) { show_help = v != null; })
                .Add("V", "-V to display version number of this application.", delegate (string v) { show_version = v != null; })
                .Add("t:", "-t for timeout value (unit is second).", delegate (string v) { timeout = int.Parse(v) * 1000; })
                .Add("r:", "-r for retry count (default is 0)", delegate (string v) { retry = int.Parse(v); })
                .Add("v:", "-v for SNMP version (v1, v2 are currently supported)", delegate (string v)
                     {
                         switch (int.Parse(v))
                         {
                             case 1:
                                 version = VersionCode.V1;
                                 break;
                             case 2:
                                 version = VersionCode.V2;
                                 break;
                             case 3:
                                 version = VersionCode.V3;
                                 break;
                             default:
                                 throw new ArgumentException("no such version: " + v);
                         }
                     });
            
            List<string> extra = p.Parse (args);
            
            if (show_help)
            {
                Console.WriteLine("The syntax is similar to Net-SNMP. http://www.net-snmp.org/docs/man/snmpget.html");
                return;
            }
            
            if (show_version)
            {
                Console.WriteLine(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                return;
            }

            if (extra.Count < 2)
            {
                Console.WriteLine("The syntax is similar to Net-SNMP. http://www.net-snmp.org/docs/man/snmpget.html");
                return;
            }
            
            IPAddress ip;
            bool parsed = IPAddress.TryParse(extra[0], out ip);
            if (!parsed)
            {
                foreach (IPAddress address in Dns.GetHostAddresses(extra[0]))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ip = address;
                        break;
                    }
                }
                
                if (ip == null)
                {
                    Console.WriteLine("invalid host or wrong IP address found: " + extra[0]);
                    return;
                }
            }

            try
            {
                List<Variable> vList = new List<Variable>();
                for (int i = 1; i < extra.Count; i++)
                {
                    Variable test = new Variable(new ObjectIdentifier(extra[i]));
                    vList.Add(test);
                }

                IPEndPoint receiver = new IPEndPoint(ip, 161);
                if (version != VersionCode.V3)
                {
                    GetBulkRequestMessage message = new GetBulkRequestMessage(0,
                                                                              version,
                                                                              new OctetString(community),
                                                                              nonRepeaters,
                                                                              maxRepetitions,
                                                                              vList);
                    ISnmpMessage response = message.GetResponse(timeout, receiver);
                    if (response.Pdu.ErrorStatus.ToInt32() != 0) // != ErrorCode.NoError
                    {
                        throw SharpErrorException.Create(
                            "error in response",
                            receiver.Address,
                            response);
                    }

                    foreach (Variable variable in response.Pdu.Variables)
                    {
                        Console.WriteLine(variable);
                    }

                    return;
                }

                IAuthenticationProvider auth;
                if ((level & SecurityLevel.Authentication) == SecurityLevel.Authentication)
                {
                    auth = GetAuthenticationProviderByName(authentication, authPhrase);
                }
                else
                {
                    auth = DefaultAuthenticationProvider.Instance;
                }

                IPrivacyProvider priv;
                if ((level & SecurityLevel.Privacy) == SecurityLevel.Privacy)
                {
                    priv = new DESPrivacyProvider(new OctetString(privPhrase), auth);
                }
                else
                {
                    priv = DefaultPrivacyProvider.Instance;
                }
                
                Discovery discovery = new Discovery(1, 101);
                ReportMessage report = discovery.GetResponse(timeout, receiver);

                ProviderPair record = new ProviderPair(auth, priv);
                GetBulkRequestMessage request = new GetBulkRequestMessage(VersionCode.V3, 100, 0, new OctetString(user), nonRepeaters, maxRepetitions, vList, record, report);

                ISnmpMessage reply = request.GetResponse(timeout, receiver);
                if (reply.Pdu.ErrorStatus.ToInt32() != 0) // != ErrorCode.NoError
                {
                    throw SharpErrorException.Create(
                        "error in response",
                        receiver.Address,
                        reply);
                }

                foreach (Variable v in reply.Pdu.Variables)
                {
                    Console.WriteLine(v);
                }
            }
            catch (SharpSnmpException ex)
            {
                if (ex is SharpOperationException)
                {
                    Console.WriteLine((ex as SharpOperationException).Details);
                }
                else
                {
                    Console.WriteLine(ex);
                }
            }
        }

        private static IAuthenticationProvider GetAuthenticationProviderByName(string authentication, string phrase)
        {
            if (authentication.ToUpper() == "MD5")
            {
                return new MD5AuthenticationProvider(new OctetString(phrase));
            }

            if (authentication.ToUpper() == "SHA")
            {
                return new SHA1AuthenticationProvider(new OctetString(phrase));
            }

            throw new ArgumentException("unknown name", "authentication");
        }
    }
}