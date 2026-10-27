/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Smartphone <https://github.com/OpenChargingCloud/Smartphone>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Norn.NTS;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.SecureShell;

using cloud.charging.open.Smartphone.Charging;
using cloud.charging.open.Smartphone.Map;

#endregion

namespace cloud.charging.open.Smartphone
{

    /// <summary>
    /// The smartphone of an EV driver: a WWCP node that stands in for the
    /// apps on Android and iOS, with its JSON API at "/api" and its web
    /// interface at "/".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The web interface is a bundle of HTML, CSS and JavaScript built by
    /// webpack from Frontend/ and embedded into this assembly, so that the
    /// smartphone is one file to deploy and needs nothing installed beside it.
    /// The browser and the smartphone talk over the JSON API and one
    /// Server-Sent Events stream; nothing is rendered on the server.
    /// </para>
    /// <para>
    /// What every program of this family has - the sign-in, the name servers,
    /// the time servers, the certificates it holds them to, the SSH server and
    /// the log - the node below brings. Its accounts are the people who carry
    /// it: "root", made at the first start, is the driver. What makes it a
    /// driver's phone is on top: a map of charging stations, and charging
    /// sessions started from it and watched while they happen - simulated,
    /// until there is an account at an e-mobility provider to start real ones
    /// with.
    /// </para>
    /// <para>
    /// What it believes is the important part already. An app talks to its
    /// provider over TLS and to nobody else, and which root CAs it believes for
    /// that is the one decision about trust it makes - so the node's own store
    /// keeps the roots, the server certificates it can hold a server to by
    /// fingerprint, and who it is as a client, and nothing of ISO 15118, which
    /// is the vehicle's.
    /// </para>
    /// </remarks>
    public partial class Smartphone : WWCPNode
    {

        #region Data

        /// <summary>
        /// The manifest resource prefix of the embedded frontend bundle
        /// (see the EmbedFrontend target of Smartphone.csproj).
        /// </summary>
        public const            String    HTTPRoot         = "cloud.charging.open.Smartphone.HTTPRoot.";

        /// <summary>
        /// The TCP port the web interface listens on, unless another is given.
        /// </summary>
        /// <remarks>
        /// One below the vehicle's 2347, because the driver's phone is the
        /// thing next to the vehicle - and clear of the station's 2348, the
        /// local controller's 2350, the CSMS's 2351, the gateway's 2353, the
        /// e-mobility provider's 2355, the hub's 2356 and the PKI's 2360, so
        /// that a smartphone started on the same bench as all of them does not
        /// fight any of them over a port.
        /// </remarks>
        public static new readonly  IPPort    DefaultHTTPPort  = IPPort.Parse(2346);

        /// <summary>
        /// What kind of node a smartphone is.
        /// </summary>
        /// <remarks>
        /// "Smartphone" after "OpenChargingCloud" in the Server header,
        /// "smartphone-2026-10-09.log" for a day's log file, and "Smartphone"
        /// as the organization its accounts are in - which a first start writes
        /// into the accounts file and every start after it reads back, so it is
        /// the one that must never change.
        /// </remarks>
        public static readonly      NodeKind  SmartphoneKind   = new (
                                                                     Name:           "smartphone",
                                                                     Tag:            "smartphone",
                                                                     Product:        "Smartphone",
                                                                     Organization:   "Smartphone",
                                                                     LogFilePrefix:  "smartphone"
                                                                 );

        /// <summary>
        /// The kinds of certificate the smartphone's store keeps: the roots it
        /// believes, the server certificates it holds a server to, who it is
        /// as a client, and who its web interface is over HTTPS.
        /// </summary>
        /// <remarks>
        /// Four of the five kinds of TLS: nobody connects to a smartphone with
        /// a certificate of their own, so there is no root of clients to keep.
        /// The server identity is for "web.tls.certificate": "store" - a
        /// browser tells a page where this device is only over HTTPS or from
        /// the machine itself, and Locate me asks it. None of the seven of
        /// ISO 15118, which are a vehicle's.
        /// </remarks>
        public static readonly      IReadOnlyList<CertificateKind>  CertificateKinds  = [
                                                                                          CertificateKind.TLSRoot,
                                                                                          CertificateKind.TLSServer,
                                                                                          CertificateKind.TLSIdentity,
                                                                                          CertificateKind.TLSServerIdentity
                                                                                      ];

        /// <summary>
        /// Where the tiles of the map come from.
        /// </summary>
        /// <remarks>
        /// OpenStreetMap's own tile server, which asks of an application to say
        /// where its tiles are from - the map does, in its corner - and to stay
        /// light: one phone and its driver are.
        /// </remarks>
        public const                String    MapTileServer    = "https://tile.openstreetmap.org";

        /// <summary>
        /// The security headers the web interface is served with: the node's
        /// strict ones, and on top what the map needs - images from
        /// <see cref="MapTileServer"/>, and the browser's position for "where
        /// am I", asked by this page only.
        /// </summary>
        public static readonly      SecurityHeaderOptions  FrontendSecurityHeaders  = SecurityHeaderOptions.Default with {
                                                                                          ContentSecurityPolicy  = SecurityHeaderOptions.DefaultContentSecurityPolicy.
                                                                                                                       Replace("img-src 'self' data:", $"img-src 'self' data: {MapTileServer}"),
                                                                                          PermissionsPolicy      = "camera=(), microphone=(), geolocation=(self)"
                                                                                      };

        #endregion

        #region Properties

        /// <summary>
        /// The JSON API the browser talks to.
        /// </summary>
        public SmartphoneHTTPAPI  API       { get; }

        /// <summary>
        /// Every charging session of this phone, running and past.
        /// </summary>
        public ChargingSessions   Sessions  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// One smartphone, with its web interface.
        /// </summary>
        /// <param name="HTTPHostname">The address the web interface listens on; 127.0.0.1 by default.</param>
        /// <param name="HTTPPort">The TCP port it listens on; <see cref="DefaultHTTPPort"/> by default.</param>
        /// <param name="HTTPServer">An HTTP server to register within, or null to make one.</param>
        /// <param name="BasePath">What everything of this smartphone sits below; the root by default. Something else only where several of these programs share one HTTP server.</param>
        /// <param name="HTTPRootPath">Where the JSON API sits; "/api" below <paramref name="BasePath"/> by default.</param>
        /// <param name="ExtAPI">An HTTPExt API to sign in against, or null for one of this smartphone's own. Handing one in is what makes one sign-in open several of these programs at once.</param>
        /// <param name="AccountsPath">The directory the accounts live in between starts.</param>
        /// <param name="ConfigFile">Where the configuration lives between starts: one file, whose sections the node below reads.</param>
        /// <param name="DNSClient">How to resolve names, or null to make a client.</param>
        /// <param name="NTSClient">Where to read the time, or null to make a client.</param>
        /// <param name="Frontend">Where the web interface comes from, or null for the embedded bundle.</param>
        /// <param name="CertificatesPath">The directory the certificate store lives in between starts; what the configuration file says, or "certificates" beside it, by default.</param>
        /// <param name="Log">Where everything that happens is written, or null to make a log.</param>
        /// <param name="LogToConsole">Whether the log is also written to the console.</param>
        /// <param name="ConsoleLogLevel">How much of it reaches the console.</param>
        /// <param name="LogPath">The directory the log files are written to, or null to write none.</param>
        /// <param name="BridgeDebugLog">Whether what the libraries below write with DebugX is picked up.</param>
        /// <param name="TimeProvider">The clock, or null for the system one.</param>
        /// <param name="SSH">What the program says about serving the command line over SSH; nothing by default - see SSHSettings.</param>
        /// <param name="Web">What the program says of HTTPS and of the reverse proxies in front - from --https, --https-cert, --redirect-port and --trusted-proxy - over the "web" section of the configuration file; nothing by default, and then only the file says it.</param>
        /// <param name="ChargingSessionsPath">The file the charging sessions are kept in; "charging-sessions.json" beside the configuration file by default.</param>
        public Smartphone(IIPAddress?            HTTPHostname       = null,
                          IPPort?                HTTPPort           = null,
                          HTTPServer?            HTTPServer         = null,
                          HTTPPath?              BasePath           = null,
                          HTTPPath?              HTTPRootPath       = null,
                          HTTPExtAPI?            ExtAPI             = null,
                          String?                AccountsPath       = null,
                          WWCPConfigFile?        ConfigFile         = null,
                          DNSClient?             DNSClient          = null,
                          NTSClient?             NTSClient          = null,
                          IStaticContentSource?  Frontend           = null,
                          String?                CertificatesPath   = null,
                          EventLog?              Log                = null,
                          Boolean                LogToConsole       = true,
                          LogLevel               ConsoleLogLevel    = LogLevel.Info,
                          String?                LogPath            = null,
                          Boolean                BridgeDebugLog     = true,
                          TimeProvider?          TimeProvider       = null,
                          SSHSettings?           SSH                = null,
                          WebConfiguration?      Web                = null,
                          String?                ChargingSessionsPath  = null)

            : base(Kind:               SmartphoneKind,
                   Version:            typeof(Smartphone).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                   HTTPPort:           HTTPPort ?? DefaultHTTPPort,
                   HTTPHostname:       HTTPHostname,
                   HTTPServer:         HTTPServer,
                   BasePath:           BasePath,
                   HTTPRootPath:       HTTPRootPath,
                   ExtAPI:             ExtAPI,
                   AccountsPath:       AccountsPath,
                   ConfigFile:         ConfigFile,
                   DNSClient:          DNSClient,
                   NTSClient:          NTSClient,
                   Frontend:           Frontend ?? new EmbeddedContentSource(HTTPRoot, typeof(Smartphone).Assembly),
                   Resources:          SmartphoneAccess.Resources,
                   RoleDefinitions:    SmartphoneAccess.Roles,
                   CertificatesPath:   CertificatesPath,
                   CertificateKinds:   CertificateKinds,
                   Log:                Log,
                   LogToConsole:       LogToConsole,
                   ConsoleLogLevel:    ConsoleLogLevel,
                   LogPath:            LogPath,
                   BridgeDebugLog:     BridgeDebugLog,
                   TimeProvider:       TimeProvider,
                   SSH:                SSH,
                   FrontendSecurityHeaders:  FrontendSecurityHeaders,
                   Web:                Web)

        {

            #region The map and the charging sessions

            this.map       = LoadMapConfiguration();

            // Beside the configuration file rather than below the working
            // directory, for the reason the certificate store is: a phone that
            // forgot its sessions because "dotnet run" was typed somewhere else
            // would be a different phone.
            this.Sessions  = new ChargingSessions(
                                 ChargingSessionsPath ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(this.ConfigFile.Path)) ?? ".", ChargingSessions.DefaultFileName),
                                 this.TimeProvider
                             );

            foreach (var problem in Sessions.ReadProblems)
                this.Log.Warning(problem, "charging");

            this.Log.Info($"The map shows {DemoLocations.All.Count} demo charging stations" +
                          (map.StationsURL.Length > 0
                               ? $"; its data source is set to '{map.StationsURL}', which is not asked yet."
                               : ", as no data source is set."),
                          "map");

            this.Log.Info($"{Sessions.All().Count} charging session(s) are kept in '{Sessions.FilePath}'. " +
                           "Charging is simulated: there is no e-mobility provider to start a real session at yet.",
                          "charging");

            #endregion

            #region The JSON API

            this.Log.Info(
                OwnsExtAPI
                    ? $"The accounts of this smartphone are in '{this.ExtAPI.DatabaseFileName}', its HTTPExt API at '{this.ExtAPI.RootPath}'."
                    : $"This smartphone signs in against accounts it shares, at '{this.ExtAPI.RootPath}'.",
                "web", "http"
            );

            // The JSON API at "/api", beside the web interface the node below
            // has already put at "/". The more specific of the two, so that an
            // unknown /api path never reaches the single-page-application
            // stub.
            this.API = new SmartphoneHTTPAPI(
                           HTTPServer:  this.HTTPServer,
                           Smartphone:  this,
                           ExtAPI:      this.ExtAPI,
                           Log:         this.Log,
                           APIPath:     this.HTTPRootPath,
                           Version:     Version
                       );

            #endregion

        }

        #endregion


        #region (protected override) OnStarted()

        /// <summary>
        /// What a smartphone says once it is up: where its API is.
        /// </summary>
        protected override Task OnStarted()
        {

            Log.Info($"The JSON API is at {APIURL}v1/status", "web", "http");

            return Task.CompletedTask;

        }

        #endregion


        #region ConfigurationJSON()

        /// <summary>
        /// What this smartphone is, as the Configuration page of the web
        /// interface reads it: what the node below says of itself - the
        /// assemblies it was built from, one line per repository - and on top
        /// the smartphone.
        /// </summary>
        public override JObject ConfigurationJSON()
        {

            var json = base.ConfigurationJSON();

            // First, because it is the card the page leads with.
            json.AddFirst(new JProperty("smartphone", new JObject(
                              new JProperty("version",        Version),
                              new JProperty("createdAt",      CreatedAt.ToString("o")),
                              new JProperty("machine",        Environment.MachineName),
                              new JProperty("runtime",        Environment.Version.ToString()),
                              new JProperty("os",             Environment.OSVersion.ToString())
                          )));

            return json;

        }

        #endregion

    }

}
