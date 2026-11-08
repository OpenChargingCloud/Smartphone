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

using System.Collections.Concurrent;
using System.Threading.Channels;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.Logging;

using EVSEStatus = cloud.charging.open.Smartphone.Map.EVSEStatus;

#endregion

namespace cloud.charging.open.Smartphone.Roaming
{

    /// <summary>
    /// The status of the EVSEs of a roaming network as it says it now,
    /// followed over the WebSocket of its history at the URLs of its trust
    /// anchor - so that the map shows which of them is free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A status is runtime data, not content: nobody signs or attests it, and
    /// a proof holds whatever it is. Like the list of pools, it is only where
    /// to look - a charge point free on the map may be taken when the driver
    /// gets there - and it is taken from the network alone. Where the network
    /// is not reached, or no longer, no status is known: none is kept from a
    /// connection that ended, as nothing says how old it has grown since.
    /// </para>
    /// <para>
    /// On every connection the topics of the admin status and of the status
    /// are subscribed to first, and then the runtime status asked for as a
    /// whole, so that no change falls between the two. Of each EVSE the latest
    /// status wins by its timestamp, whichever of them came first.
    /// </para>
    /// <para>
    /// The URLs are tried in their order, the next where one does not answer,
    /// after a pause that grows from a second to a minute; one that answered
    /// and lost its connection is asked again first, after a second.
    /// </para>
    /// </remarks>
    public sealed class RoamingNetworkStatus : IAsyncDisposable
    {

        #region Data

        /// <summary>
        /// Where the WebSocket of a history is, below a URL of a trust anchor:
        /// ~/RNs/{id}/history/ws (WWCP_POI's README, "WebSocket").
        /// </summary>
        public const            String    WebSocketPath      = "history/ws";

        /// <summary>
        /// The type of the entities whose status is followed.
        /// </summary>
        public const            String    EVSEEntityType     = "EVSE";

        /// <summary>
        /// How long a URL is given to connect and to tell the runtime status.
        /// </summary>
        public static readonly  TimeSpan  DefaultTimeout     = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The pause before asking again, the first time.
        /// </summary>
        public static readonly  TimeSpan  MinReconnectDelay  = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The longest pause before asking again.
        /// </summary>
        public static readonly  TimeSpan  MaxReconnectDelay  = TimeSpan.FromMinutes(1);

        private static readonly String[]  Topics             = [ POI_API.AdminStatusUpdatesTopic, POI_API.StatusUpdatesTopic ];

        private const           String    SubscribeId        = "subscribe";
        private const           String    RuntimeStatusId    = "runtimeStatus";

        private readonly IDNSClient    dnsClient;
        private readonly TimeProvider  timeProvider;
        private readonly EventLog      log;
        private readonly TimeSpan      timeout;

        private readonly ConcurrentDictionary<String, (DateTimeOffset At, String Value)>  statuses       = new();
        private readonly ConcurrentDictionary<String, (DateTimeOffset At, String Value)>  adminStatuses  = new();

        private readonly Object                    stateLock  = new();
        private          Boolean                   live;
        private          String?                   from;
        private          DateTimeOffset?           since;
        private          String?                   error;

        private          CancellationTokenSource?  following;
        private          Task?                     followingTask;

        #endregion

        #region Properties

        /// <summary>
        /// The roaming network whose status is followed.
        /// </summary>
        public RoamingNetworkTrustAnchor  TrustAnchor  { get; }

        /// <summary>
        /// Whether the status is known now: connected, and told the runtime
        /// status as a whole.
        /// </summary>
        public Boolean  IsLive
        {
            get
            {
                lock (stateLock)
                    return live;
            }
        }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Follow the status of the roaming network of the given trust anchor,
        /// once started.
        /// </summary>
        /// <param name="TrustAnchor">The roaming network, at the URLs its trust anchor names.</param>
        /// <param name="DNSClient">How to resolve the names of its URLs: the smartphone's own.</param>
        /// <param name="TimeProvider">The clock.</param>
        /// <param name="Log">Where it is said when the status is known, and when no longer.</param>
        /// <param name="Timeout">How long a URL is given to connect and to tell the runtime status.</param>
        public RoamingNetworkStatus(RoamingNetworkTrustAnchor  TrustAnchor,
                                    IDNSClient                 DNSClient,
                                    TimeProvider               TimeProvider,
                                    EventLog                   Log,
                                    TimeSpan?                  Timeout   = null)
        {

            this.TrustAnchor   = TrustAnchor;
            this.dnsClient     = DNSClient;
            this.timeProvider  = TimeProvider;
            this.log           = Log;
            this.timeout       = Timeout ?? DefaultTimeout;

        }

        #endregion


        #region Start() / DisposeAsync()

        /// <summary>
        /// Start following, until disposed.
        /// </summary>
        public void Start()
        {

            lock (stateLock)
            {

                if (following is not null)
                    return;

                following      = new CancellationTokenSource();
                followingTask  = FollowAsync(following.Token);

            }

        }

        /// <summary>
        /// Stop following: the connection is closed, and no status is known.
        /// </summary>
        public async ValueTask DisposeAsync()
        {

            CancellationTokenSource?  stop;
            Task?                     task;

            lock (stateLock)
            {
                stop       = following;
                task       = followingTask;
                following  = null;
            }

            if (stop is null)
                return;

            await stop.CancelAsync();

            if (task is not null)
            {
                try
                {
                    await task;
                }
                catch (Exception)
                {
                    // Followed no more, whatever ended it.
                }
            }

            stop.Dispose();

        }

        #endregion


        #region StatusOf(EVSEId)

        /// <summary>
        /// The status of an EVSE as the map shows it, in OCPI's words - or null
        /// where none is known: the network not reached, or saying nothing of
        /// it.
        /// </summary>
        public String? StatusOf(String EVSEId)
        {

            if (!IsLive)
                return null;

            var admin   = adminStatuses.TryGetValue(EVSEId, out var adminStatus) ? adminStatus.Value : null;
            var status  = statuses.     TryGetValue(EVSEId, out var anyStatus)   ? anyStatus.  Value : null;

            if (admin is null && status is null)
                return null;

            return OCPIStatusOf(admin, status);

        }

        #endregion

        #region (static) OCPIStatusOf(AdminStatus, Status)

        /// <summary>
        /// The status of an EVSE in OCPI's words, of its admin status and its
        /// status in POI's: what its operator declares comes first - an EVSE
        /// out of service is so whatever it says of itself - and then what it
        /// is, grouped as POI's group status groups it.
        /// </summary>
        /// <param name="AdminStatus">The admin status, e.g. "operational" or "outOfService".</param>
        /// <param name="Status">The status, e.g. "available" or "charging".</param>
        public static String OCPIStatusOf(String?  AdminStatus,
                                          String?  Status)
        {

            switch (AdminStatus?.ToLowerInvariant())
            {

                case "outofservice":
                    return EVSEStatus.Inoperative;

                // Not for the public: as a charge point blocked by a car that
                // does not charge, nowhere to start.
                case "blocked":
                case "internaluse":
                    return EVSEStatus.Blocked;

                case "planned":
                case "indeployment":
                    return EVSEStatus.Planned;

            }

            return Status?.ToLowerInvariant() switch {

                       "available"       or "waitingforplugin"                   => EVSEStatus.Available,
                       "charging"        or "pluggedin"         or "occupied"    => EVSEStatus.Charging,
                       "reserved"                                                => EVSEStatus.Reserved,
                       "blocked"                                                 => EVSEStatus.Blocked,
                       "inoperative"     or "unavailable"       or "outofservice"=> EVSEStatus.Inoperative,
                       "outoforder"      or "error"             or "offline"     or
                       "doornotclosed"                                           => EVSEStatus.OutOfOrder,
                       "planned"         or "indeployment"                       => EVSEStatus.Planned,
                       "removed"                                                 => EVSEStatus.Removed,
                       _                                                         => EVSEStatus.Unknown

                   };

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// Whether the status is known, from where and since when - or why not.
        /// </summary>
        public JObject ToJSON()
        {

            lock (stateLock)
                return new JObject(
                           new JProperty("live",   live),
                           new JProperty("from",   from),
                           new JProperty("since",  since?.ToString("o")),
                           new JProperty("error",  error),
                           new JProperty("evses",  live ? statuses.Keys.Union(adminStatuses.Keys).Count() : 0)
                       );

        }

        #endregion


        #region (static) WebSocketURLOf(URL)

        /// <summary>
        /// The WebSocket of the history of a roaming network, of a URL its trust
        /// anchor names: "https://hub/RNs/rn1" is "wss://hub/RNs/rn1/history/ws".
        /// </summary>
        public static String WebSocketURLOf(String URL)
        {

            var text = URL.TrimEnd('/');

            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                text = "wss://" + text["https://".Length..];

            else if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                text = "ws://"  + text["http://".Length..];

            return $"{text}/{WebSocketPath}";

        }

        #endregion

        #region (private) FollowAsync(CancellationToken)

        /// <summary>
        /// Connected to one URL after the other, until stopped.
        /// </summary>
        private async Task FollowAsync(CancellationToken CancellationToken)
        {

            var urls      = TrustAnchor.URLs.Select(url => url.ToString()).ToArray();
            var next      = 0;
            var failures  = 0;

            if (urls.Length == 0)
                return;

            while (!CancellationToken.IsCancellationRequested)
            {

                var url = WebSocketURLOf(urls[next % urls.Length]);

                Boolean  wasLive;
                String   problem;

                try
                {
                    (wasLive, problem) = await FollowAtAsync(url, CancellationToken);
                }
                catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    (wasLive, problem) = (false, $"{url} could not be reached: {e.Message}");
                }

                if (CancellationToken.IsCancellationRequested)
                    break;

                Lost(problem);

                // One that answered is asked again first; one that did not
                // gives way to the next, and the pause grows.
                if (wasLive)
                {
                    failures = 0;
                    log.Warning($"The status of the charge points of the roaming network '{TrustAnchor.Id}' is not known any more: {problem}", "map", "poi");
                }
                else
                {

                    if (failures == 0)
                        log.Warning($"The status of the charge points of the roaming network '{TrustAnchor.Id}' is not known: {problem}", "map", "poi");

                    failures++;
                    next++;

                }

                var delay = wasLive
                                ? MinReconnectDelay
                                : TimeSpan.FromTicks(Math.Min(MaxReconnectDelay.Ticks,
                                                              MinReconnectDelay.Ticks * (1L << Math.Min(failures - 1, 16))));

                try
                {
                    await Task.Delay(delay, timeProvider, CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

            }

            Lost(null);

        }

        #endregion

        #region (private) FollowAtAsync(URL, CancellationToken)

        /// <summary>
        /// Connected to one URL, for as long as the connection lasts: whether
        /// it told the runtime status, and why it ended.
        /// </summary>
        private async Task<(Boolean WasLive, String Problem)> FollowAtAsync(String             URL,
                                                                             CancellationToken  CancellationToken)
        {

            // What Hermod's events hand over, read here one after the other.
            var messages     = Channel.CreateUnbounded<String>(new UnboundedChannelOptions { SingleReader = true });
            var closedWith   = (String?) null;
            var wasLive      = false;

            await using var client = new WebSocketClient(org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.Parse(URL),
                                                         RequestTimeout:  timeout,
                                                         ConnectTimeout:  timeout,
                                                         DNSClient:       dnsClient);

            client.OnTextMessageReceived  += (timestamp, webSocketClient, connection, frame, eventTrackingId, text, cancellationToken) => {
                messages.Writer.TryWrite(text);
                return Task.CompletedTask;
            };

            client.OnCloseMessageReceived += (timestamp, webSocketClient, connection, frame, eventTrackingId, statusCode, reason, cancellationToken) => {
                closedWith = $"{(Int32) statusCode} {statusCode}" + (String.IsNullOrWhiteSpace(reason) ? "" : $": {reason}");
                messages.Writer.TryComplete();
                return Task.CompletedTask;
            };

            try
            {

                var (_, response) = await client.Connect(CancellationToken: CancellationToken).WaitAsync(timeout, CancellationToken);

                if (response is null || response.HTTPStatusCode != HTTPStatusCode.SwitchingProtocols)
                    return (false, $"{URL} answered {response?.HTTPStatusCode.ToString() ?? "nothing"}, and no WebSocket.");

                await client.SendTextMessage(POIWebSocketMessage.Request(SubscribeId,     "subscribe",     new JObject(new JProperty("topics", new JArray(Topics)))).ToJSONText(), CancellationToken: CancellationToken);
                await client.SendTextMessage(POIWebSocketMessage.Request(RuntimeStatusId, "runtimeStatus", new JObject()).ToJSONText(), CancellationToken: CancellationToken);

                using var untilTold = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

                untilTold.CancelAfter(timeout);

                while (true)
                {

                    String text;

                    try
                    {
                        text = await messages.Reader.ReadAsync(wasLive ? CancellationToken : untilTold.Token);
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    catch (OperationCanceledException) when (!CancellationToken.IsCancellationRequested)
                    {
                        return (false, $"{URL} did not tell the runtime status within {timeout.TotalSeconds:0} seconds.");
                    }

                    if (!POIWebSocketMessage.TryParse(text, out var message, out _))
                        continue;

                    switch (message.MessageType)
                    {

                        case POIWebSocketMessageType.Response when message.MessageId == RuntimeStatusId:

                            foreach (var json in (message.Payload?["runtimeStatus"] as JArray ?? []).OfType<JObject>())
                                Take(json);

                            wasLive = true;
                            Told(URL);
                            break;

                        case POIWebSocketMessageType.RequestError:
                            return (wasLive, $"{URL} refused {(message.MessageId == SubscribeId ? "the subscription to the status" : "to tell the runtime status")}: {message.ErrorCode} {message.ErrorDescription}");

                        case POIWebSocketMessageType.Send when message.Payload is JObject payload:

                            // A push, or pushes that waited together as one batch.
                            foreach (var json in payload["batch"] is JArray batch ? batch.OfType<JObject>() : [ payload ])
                                Take(json);

                            break;

                    }

                }

                return (wasLive, closedWith is not null
                                     ? $"{URL} closed the connection: {closedWith}."
                                     : $"The connection to {URL} ended.");

            }
            finally
            {
                try
                {
                    await client.Close();
                }
                catch (Exception)
                {
                    // Nothing left to close of a connection already gone.
                }
            }

        }

        #endregion

        #region (private) Take(JSON)

        /// <summary>
        /// The admin status or the status of an EVSE, where it is later than
        /// the one held.
        /// </summary>
        private void Take(JObject JSON)
        {

            if (!POIStatusUpdate.TryParse(JSON, out var update, out _) ||
                update.EntityType != EVSEEntityType)
                return;

            var held = update.Kind switch {
                           POIRuntimeStatusKind.Status       => statuses,
                           POIRuntimeStatusKind.AdminStatus  => adminStatuses,
                           _                                 => null
                       };

            held?.AddOrUpdate(update.EntityId,
                              (update.Timestamp, update.Value),
                              (_, before) => update.Timestamp >= before.At ? (update.Timestamp, update.Value) : before);

        }

        #endregion

        #region (private) Told(URL) / Lost(Problem)

        private void Told(String URL)
        {

            var count = statuses.Keys.Union(adminStatuses.Keys).Count();

            lock (stateLock)
            {
                live   = true;
                from   = URL;
                since  = timeProvider.GetUtcNow();
                error  = null;
            }

            log.Info($"The status of the charge points of the roaming network '{TrustAnchor.Id}' is followed at {URL}: {count} EVSE(s) say theirs.", "map", "poi");

        }

        private void Lost(String? Problem)
        {

            lock (stateLock)
            {
                live   = false;
                from   = null;
                since  = null;
                error  = Problem;
            }

            statuses.     Clear();
            adminStatuses.Clear();

        }

        #endregion

    }

}
