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
using System.Diagnostics.CodeAnalysis;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.chargy;
using cloud.charging.open.chargy.Formats.OCMF;
using cloud.charging.open.chargy.IO;
using cloud.charging.open.chargy.LiveLink;

using cloud.charging.open.Smartphone.Charging;

using ChargingSession  = cloud.charging.open.Smartphone.Charging.ChargingSession;

#endregion

namespace cloud.charging.open.Smartphone
{

    public partial class Smartphone
    {

        #region Data

        /// <summary>
        /// How long to wait before a live link none of whose endpoints answered
        /// is asked again, at first: doubled every time, up to
        /// <see cref="LiveLinkRetryMax"/>.
        /// </summary>
        public static readonly TimeSpan  LiveLinkRetryFirst    = TimeSpan.FromSeconds(1);

        /// <summary>
        /// How long to wait at most before a live link none of whose endpoints
        /// answered is asked again.
        /// </summary>
        public static readonly TimeSpan  LiveLinkRetryMax      = TimeSpan.FromMinutes(1);

        /// <summary>
        /// How long the document of a live link is waited for, where its URL
        /// was handed over.
        /// </summary>
        public static readonly TimeSpan  LiveLinkFetchTimeout  = TimeSpan.FromSeconds(30);

        /// <summary>
        /// ChargyCore's pipeline every update of a live link goes through, with
        /// every format it reads.
        /// </summary>
        private static readonly Lazy<ContentFormatDetector> liveLinkDetector = new (() => new ContentFormatDetector(I18NDictionary.Default(),
                                                                                                                    ChargeTransparencyFormats.All(I18NDictionary.Default())));

        /// <summary>
        /// The live links followed now, by the session.
        /// </summary>
        private readonly ConcurrentDictionary<String, (CancellationTokenSource Stop, Task Following)>  liveLinkFollowers  = new();

        private readonly Object   liveLinksLock  = new();
        private          Boolean  followsLiveLinks;

        #endregion


        #region FollowLiveLinkAsync(LocationId, EVSEUid, LiveLink, Who, StopLink = null, Limit = null, CancellationToken = default)

        /// <summary>
        /// Take over the live link of a charging session that the web payment
        /// page of the operator of the station started - paid there, without an
        /// e-mobility provider - and follow it. Only at an EVSE of a station of
        /// a roaming network whose proof was checked and still holds: the
        /// session keeps its evidence and its price, as one this phone starts.
        /// With the stop link the page gave, where it gave one, to stop it
        /// with, and the limit the driver chose there.
        /// </summary>
        /// <param name="LocationId">The station, by the id the map gave it.</param>
        /// <param name="EVSEUid">Its EVSE, by the uid the map gave it.</param>
        /// <param name="LiveLink">The live link the page gave: the https URL of its document, or the document itself, as text or JSON.</param>
        /// <param name="Who">Who handed it over, for the log and the session.</param>
        /// <param name="StopLink">The stop link the page gave: an https URL of this session alone, which a POST stops it at.</param>
        /// <param name="Limit">The limit the driver chose at the page, as OCPP 2.1 says it to the station.</param>
        /// <param name="CancellationToken">To stop fetching its document.</param>
        public async Task<ChargingStart> FollowLiveLinkAsync(String             LocationId,
                                                             String             EVSEUid,
                                                             JToken             LiveLink,
                                                             String             Who,
                                                             String?            StopLink           = null,
                                                             TransactionLimit?  Limit              = null,
                                                             CancellationToken  CancellationToken  = default)
        {

            if (StopLink is not null && !TryLinkURL(StopLink, out _))
                return ChargingStart.Refused(ChargingRefusal.Invalid, $"'{StopLink}' is no stop link: it is the https URL the web payment page gave to stop the session with.");

            var network = RoamingNetwork;

            if (network.TrustAnchor is null)
                return ChargingStart.Refused(ChargingRefusal.Conflict, "A live link is followed at a station of a roaming network whose proof was checked, and no roaming network is set: set the trust anchor of its network first.");

            var location = RoamingLocation(LocationId);

            if (location is null)
                return ChargingStart.Refused(ChargingRefusal.NotFound, $"There is no charging station '{LocationId}' on the map.");

            var evse = location.EVSE(EVSEUid);

            if (evse is null)
                return ChargingStart.Refused(ChargingRefusal.NotFound, $"{location.Name} has no charge point '{EVSEUid}'.");

            var (station, tariff, refusal) = CheckedStationOf(location, evse, network, TimeProvider.GetUtcNow());

            if (station is null || tariff is null)
                return ChargingStart.Refused(ChargingRefusal.Conflict, refusal ?? $"{evse.EVSEId} at {location.Name} was not checked.");

            if (Sessions.IsCharging(location.Id, evse.Uid, TimeProvider.GetUtcNow()))
                return ChargingStart.Refused(ChargingRefusal.Conflict, $"This phone is already charging at {evse.EVSEId}.");

            var (document, from, problem, unreachable) = await LiveLinkDocumentAsync(LiveLink, CancellationToken);

            if (document is null)
                return ChargingStart.Refused(unreachable ? ChargingRefusal.Unreachable : ChargingRefusal.Invalid,
                                             problem ?? "No live link was handed over.");

            var follow = new LiveLinkFollow(document, from, TimeProvider.GetUtcNow(), StopLink: StopLink?.Trim(), Limit: Limit);

            if (follow.EVSEId is String named && named != evse.EVSEId)
                return ChargingStart.Refused(ChargingRefusal.Conflict, $"The live link is of {named}, not of {evse.EVSEId} at {location.Name}: it was handed over at the wrong charge point.");

            if (!Sessions.TryStart(location with { Tariff = tariff }, evse, (one, _) => RoamingStatusOf(one), Who, null, null,
                                   out var session, out var error, out var notSaved, EvidenceOf(station, evse), follow))
                return ChargingStart.Refused(notSaved ? ChargingRefusal.NotSaved : ChargingRefusal.Conflict, error);

            Log.Notice($"'{Who}' handed over the live link of a session at {location.Name}, {evse.EVSEId}, which the web payment page of its operator started" +
                       (from is not null ? $", from {from}" : "") +
                       $", for {session.PricePerKWh:0.00##} {session.Currency} per kWh, as its checked proof says" +
                       (follow.StopLinkHost is String stopAt ? $", with a stop link at {stopAt}" : ", with no stop link") +
                       (Limit is not null ? $" and the limit {Limit.InWords(session.Currency)}" : "") +
                       $". The session is {session.Id}.",
                       "charging", "web");

            StartFollowing(session);

            // The proof checked before may run out before the meter signs the
            // first value: one asked for now covers the start of the session.
            if (session.Evidence?.CoveredUntil is DateTimeOffset coveredUntil && coveredUntil < TimeProvider.GetUtcNow() + 2 * ProofRefreshInterval)
                await KeepLatestProofAsync(session, "at the start", CancellationToken);

            return new ChargingStart(Sessions.Get(session.Id) ?? session, null, ChargingRefusal.None);

        }

        #endregion

        #region (private) LiveLinkDocumentAsync(LiveLink, CancellationToken)

        /// <summary>
        /// The document of the live link handed over: itself, or fetched from
        /// its URL - https, or http on this machine's loopback - and read by
        /// ChargyCore as a charge transparency live link that names how to reach
        /// it. Or why not, and whether its URL could not be reached.
        /// </summary>
        private async Task<(JObject? Document, String? From, String? Problem, Boolean Unreachable)> LiveLinkDocumentAsync(JToken             LiveLink,
                                                                                                                          CancellationToken  CancellationToken)
        {

            JObject? document  = LiveLink as JObject;
            String?  from      = null;

            if (document is null)
            {

                var text = LiveLink.Type == JTokenType.String ? LiveLink.Value<String>()?.Trim() ?? "" : "";

                if (text.StartsWith('{'))
                {
                    try
                    {
                        document = JObject.Parse(text, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    }
                    catch (JsonException e)
                    {
                        return (null, null, $"The live link is no JSON document: {e.Message}", false);
                    }
                }

                else
                {

                    if (!TryLinkURL(text, out var uri))
                        return (null, null, $"'{text}' is neither the https URL of a live link nor its document.", false);

                    from = uri.ToString();

                    try
                    {

                        await using var http = new HTTPClient(URL.Parse($"{uri.Scheme}://{uri.Authority}"), DNSClient: DNSClient);

                        var response = await http.GET(HTTPPath.Parse(uri.AbsolutePath),
                                                      QueryString:        uri.Query.Length > 1 ? QueryString.Parse(uri.Query[1..]) : null,
                                                      Accept:             AcceptTypes.FromHTTPContentTypes(HTTPContentType.Application.JSON_UTF8),
                                                      RequestTimeout:     LiveLinkFetchTimeout,
                                                      CancellationToken:  CancellationToken);

                        if (!response.HTTPStatusCode.IsSuccessful)
                            return (null, from, $"{from} answered {response.HTTPStatusCode}, not the live link.", true);

                        document = JObject.Parse(response.HTTPBodyAsUTF8String ?? "");

                    }
                    catch (JsonException e)
                    {
                        return (null, from, $"{from} answered no JSON document: {e.Message}", false);
                    }
                    catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        return (null, from, $"{from} could not be reached: {e.Message}", true);
                    }

                }

            }

            if (!ChargeTransparencyLiveLink.TryParse(document, out var liveLink))
                return (null, from, $"{(from ?? "What was handed over")} is no charge transparency live link: it names the @context '{ChargeTransparencyLiveLink.JSONLDContext}', when it was created and its liveTransports.", false);

            if (liveLink!.LiveTransports.Count == 0)
                return (null, from, "The live link names no transport to follow it over.", false);

            return (document, from, null, false);

        }

        #endregion

        #region (private static) TryLinkURL(Text, out URI)

        /// <summary>
        /// Whether the given text is a link the web payment page may give - of
        /// a live link, or to stop a session with: an https URL, or an http URL
        /// on this machine's loopback.
        /// </summary>
        private static Boolean TryLinkURL(String                         Text,
                                          [NotNullWhen(true)] out Uri?   URI)

            => Uri.TryCreate(Text.Trim(), UriKind.Absolute, out URI) &&
               (URI.Scheme == Uri.UriSchemeHttps || (URI.Scheme == Uri.UriSchemeHttp && URI.IsLoopback));

        #endregion


        #region (private) StopLiveLinkAsync(Running, Who, StopLink, CancellationToken)

        /// <summary>
        /// Stop a session the web payment page of the operator of its station
        /// started, through the stop link the page gave - handed over with its
        /// live link, or now: a POST to it, which the page takes with a 2xx.
        /// The session charges on until the meter signs its end, and its live
        /// link is followed until then.
        /// </summary>
        private async Task<ChargingStop> StopLiveLinkAsync(ChargingSession    Running,
                                                           String             Who,
                                                           String?            StopLink,
                                                           CancellationToken  CancellationToken)
        {

            var follow = Running.LiveLink!;

            if (StopLink is not null && !TryLinkURL(StopLink, out _))
                return ChargingStop.Refused(ChargingRefusal.Invalid, $"'{StopLink}' is no stop link: it is the https URL the web payment page gave to stop the session with.");

            var stopLink = StopLink?.Trim() ?? follow.StopLink;

            if (stopLink is null)
                return ChargingStop.Refused(ChargingRefusal.Conflict, $"The web payment page of its operator gave no stop link for the session at {Running.EVSEId}: stop it there, or hand its stop link over.");

            var stop    = await PostStopLinkAsync(stopLink, Who, null, CancellationToken);
            var kept    = Follow(Running.Id, kept => kept with { StopLink = stopLink, Stop = stop });
            var stopAt  = (kept?.LiveLink ?? follow with { StopLink = stopLink }).StopLinkHost;

            if (!stop.Accepted)
            {
                Log.Warning($"'{Who}' wanted to stop the session {Running.Id} at {Running.LocationName}, {Running.EVSEId}, through the stop link of the web payment page of its operator at {stopAt}, and it stopped nothing: {stop.Answer}",
                            "charging", "web");
                return ChargingStop.Refused(ChargingRefusal.Operator, $"The web payment page of its operator stopped nothing: {stop.Answer}");
            }

            if (kept is null)
                return ChargingStop.Refused(ChargingRefusal.NotSaved, $"The web payment page of its operator took the stop of the session at {Running.EVSEId}, and this phone could not keep it.");

            Log.Notice($"'{Who}' stopped the session {Running.Id} at {Running.LocationName}, {Running.EVSEId}, through the stop link of the web payment page of its operator at {stopAt}, which answered {stop.Answer}. " +
                       "This phone follows its live link until the meter signs its end.",
                       "charging", "web");

            // What the meter signs at the end is checked with a proof of now.
            if (kept.Evidence is not null)
                await KeepLatestProofAsync(kept, "at the stop", CancellationToken);

            return new ChargingStop(Sessions.Get(Running.Id) ?? kept, null, ChargingRefusal.None);

        }

        #endregion

        #region (private) StopRefusedAsync(Session, CancellationToken)

        /// <summary>
        /// Stop the station of a session this phone stopped because one of its
        /// signed values is a wrong one, through the stop link of the web
        /// payment page, where the page gave one and it was not asked yet.
        /// </summary>
        private async Task StopRefusedAsync(ChargingSession    Session,
                                            CancellationToken  CancellationToken)
        {

            if (Session.LiveLink is not LiveLinkFollow { Refused: true, StopLink: String stopLink, Stop: null } follow)
                return;

            var stop = await PostStopLinkAsync(stopLink, "this phone", follow.RefusedBecause, CancellationToken);

            Follow(Session.Id, kept => kept with { Stop = stop });

            if (stop.Accepted)
                Log.Notice($"This phone stopped the station of the session {Session.Id} at {Session.LocationName}, {Session.EVSEId}, through the stop link of the web payment page of its operator at {follow.StopLinkHost}, which answered {stop.Answer}.",
                           "charging", "web");

            else
                Log.Warning($"This phone could not stop the station of the session {Session.Id} at {Session.LocationName}, {Session.EVSEId}, through the stop link of the web payment page of its operator at {follow.StopLinkHost}: {stop.Answer} The driver stops it at the page.",
                            "charging", "web");

        }

        #endregion

        #region (private) PostStopLinkAsync(StopLink, Who, Reason, CancellationToken)

        /// <summary>
        /// Ask the web payment page to stop a session, over the stop link it
        /// gave: a POST of a JSON object - with why, where it is not the
        /// driver who asks -, which it takes with a 2xx.
        /// </summary>
        private async Task<LiveLinkStop> PostStopLinkAsync(String             StopLink,
                                                           String             Who,
                                                           String?            Reason,
                                                           CancellationToken  CancellationToken)
        {

            var now = TimeProvider.GetUtcNow();

            if (!TryLinkURL(StopLink, out var uri))
                return new LiveLinkStop(now, Who, false, "The stop link is no https URL.");

            var body = new JObject();

            if (Reason is not null)
                body.Add(new JProperty("reason", Reason));

            try
            {

                await using var http = new HTTPClient(URL.Parse($"{uri.Scheme}://{uri.Authority}"), DNSClient: DNSClient);

                var response = await http.POST(HTTPPath.Parse(uri.AbsolutePath),
                                               Encoding.UTF8.GetBytes(body.ToString(Formatting.None)),
                                               HTTPContentType.Application.JSON_UTF8,
                                               QueryString:        uri.Query.Length > 1 ? QueryString.Parse(uri.Query[1..]) : null,
                                               Accept:             AcceptTypes.FromHTTPContentTypes(HTTPContentType.Application.JSON_UTF8),
                                               RequestTimeout:     LiveLinkFetchTimeout,
                                               CancellationToken:  CancellationToken);

                var code = (Int32) response.HTTPStatusCode.Code;

                if (code == 0)
                    return new LiveLinkStop(now, Who, false, $"{uri.Authority} could not be reached: {Said(response.HTTPBodyAsUTF8String)}");

                var said = Said(response.HTTPBodyAsUTF8String);

                return new LiveLinkStop(now,
                                        Who,
                                        response.HTTPStatusCode.IsSuccessful,
                                        $"{code} {response.HTTPStatusCode.Name}{(said.Length > 0 ? $": {said}" : "")}");

            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                return new LiveLinkStop(now, Who, false, $"{uri.Authority} could not be reached: {e.Message}");
            }

        }

        /// <summary>
        /// What a page said, for the session and the log: the "message",
        /// "description" or "error" of a JSON object, else its text - in one
        /// line, cut at 300 characters.
        /// </summary>
        private static String Said(String? Text)
        {

            var said = Text?.Trim() ?? "";

            if (said.StartsWith('{'))
            {
                try
                {
                    var json = JObject.Parse(said);
                    said = json.Value<String>("message") ?? json.Value<String>("description") ?? json.Value<String>("error") ?? said;
                }
                catch (JsonException)
                {
                    // Its text, then.
                }
            }

            said = String.Join(' ', said.Split((Char[]) [ '\r', '\n', '\t' ], StringSplitOptions.RemoveEmptyEntries)).Trim();

            return said.Length > 300 ? said[..300] + " ..." : said;

        }

        #endregion


        #region (private) StartFollowing(Session)

        /// <summary>
        /// Follow the live link of the given session, unless it is followed
        /// already or the phone does not run.
        /// </summary>
        private void StartFollowing(ChargingSession Session)
        {

            lock (liveLinksLock)
            {

                if (!followsLiveLinks || Session.LiveLink is null || liveLinkFollowers.ContainsKey(Session.Id))
                    return;

                var stop = new CancellationTokenSource();

                liveLinkFollowers[Session.Id] = (stop, Task.Run(() => FollowingAsync(Session.Id, stop.Token)));

            }

        }

        #endregion

        #region (private) FollowingAsync(Id, CancellationToken)

        /// <summary>
        /// Follow the live link of a session for as long as it is starting or
        /// charging: everything that comes over it kept with it, its signed
        /// values checked as they come - a wrong one stops the session, and
        /// its station through the stop link of the web payment page, where it
        /// gave one - and ended once the meter signs the end of the transaction
        /// or the station ends the stream: how, against the limit the driver
        /// chose, said in the log. Where none of its endpoints answers, asked
        /// again after a pause growing from <see cref="LiveLinkRetryFirst"/> to
        /// <see cref="LiveLinkRetryMax"/>.
        /// </summary>
        private async Task FollowingAsync(String             Id,
                                          CancellationToken  CancellationToken)
        {

            var retry = LiveLinkRetryFirst;

            try
            {

                while (!CancellationToken.IsCancellationRequested)
                {

                    var session = Sessions.Get(Id);

                    if (session?.LiveLink is not LiveLinkFollow follow)
                        return;

                    // Stopped here for a wrong signed value - before a restart,
                    // maybe - and its station not yet.
                    if (follow.Refused)
                    {
                        await StopRefusedAsync(session, CancellationToken);
                        return;
                    }

                    if (!session.IsActiveAt(TimeProvider.GetUtcNow()))
                        return;

                    if (!ChargeTransparencyLiveLink.TryParse(follow.Link, out var liveLink))
                    {
                        Follow(Id, kept => kept with { Problem = "Its live link can not be read any more." });
                        return;
                    }

                    var      client     = new ChargeTransparencyLiveLinkClient(liveLinkDetector.Value, DNSClient: DNSClient);
                    var      connected  = false;
                    String?  problem    = null;

                    try
                    {

                        await foreach (var update in client.Connect(liveLink!, CancellationToken: CancellationToken))
                        {

                            connected  = true;
                            retry      = LiveLinkRetryFirst;

                            var followed = CheckValues(Follow(Id, kept => kept.WithUpdate(TimeProvider.GetUtcNow(),
                                                                                          update.Transport.AsText(),
                                                                                          update.Endpoint,
                                                                                          update.Result.GetType().Name,
                                                                                          SignedTextsOf(update.Result))));

                            if (followed?.LiveLink?.Refused == true)
                            {
                                await StopRefusedAsync(followed, CancellationToken);
                                return;
                            }

                            if (followed?.LiveLink?.EndedAt is not null)
                            {
                                LogEnded(followed);
                                return;
                            }

                        }

                    }
                    catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception e)
                    {
                        problem = e.Message;
                    }

                    if (CancellationToken.IsCancellationRequested)
                        return;

                    // Something answered, and then the conversation ended: the
                    // station ended the session's stream.
                    if (connected)
                    {

                        var ended = Follow(Id, kept => kept.EndedAt is null
                                                           ? kept with { EndedAt = TimeProvider.GetUtcNow(), EndedBecause = "The station ended the stream of its live link." }
                                                           : kept);

                        if (ended is not null)
                            LogEnded(ended);

                        return;

                    }

                    Follow(Id, kept => kept with { Problem = $"None of the endpoints of its live link answered{(problem is not null ? $": {problem}" : "")}. Asked again in {retry.TotalSeconds:0} s." });

                    await Task.Delay(retry, TimeProvider, CancellationToken);

                    retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LiveLinkRetryMax.Ticks));

                }

            }
            catch (OperationCanceledException)
            {
                // The phone stops.
            }
            catch (Exception e)
            {
                Log.Warning($"The live link of the session {Id} is not followed any more: {e.Message}", "charging");
            }
            finally
            {
                lock (liveLinksLock)
                    if (liveLinkFollowers.TryRemove(Id, out var follower))
                        follower.Stop.Dispose();
            }

        }

        #endregion

        #region (private) LogEnded(Session)

        /// <summary>
        /// Say in the log that a session followed over its live link ended,
        /// and how, against the limit the driver chose.
        /// </summary>
        private void LogEnded(ChargingSession Session)
        {

            var ending = LiveLinkEnding.Of(Session);

            Log.Notice($"The session {Session.Id} at {Session.LocationName}, {Session.EVSEId}, ended as its live link says: {Session.LiveLink?.EndedBecause}" +
                       (ending is not null ? $" {ending.Because}" : ""),
                       "charging");

        }

        #endregion

        #region (private) CheckValues(Session)

        /// <summary>
        /// The signed values that came over the live link of the given session,
        /// each checked once it came (<see cref="SessionCheck.ValuesOf"/>) and
        /// kept with it - checked again with every update while it could not
        /// be decided. A wrong one stops the session: it is said in the log,
        /// and its live link is followed no more. Its station is stopped
        /// through the stop link of the web payment page, where it gave one
        /// (<see cref="StopRefusedAsync"/>) - else the driver stops it there.
        /// </summary>
        private ChargingSession? CheckValues(ChargingSession? Session)
        {

            if (Session?.LiveLink is not LiveLinkFollow follow)
                return Session;

            // Not decided again, for the same reason: not kept again.
            var checks = SessionCheck.ValuesOf(RoamingNetwork, Session, TimeProvider.GetUtcNow()).
                                      Where(check => check.Check.Holds is not null || check.Document.Check?.Error != check.Check.Error || check.Document.Check is null).
                                      ToArray();

            if (checks.Length == 0)
                return Session;

            var checkedSession = Follow(Session.Id, kept => kept.WithChecks(checks.Select(check => (check.Document.Text, check.Check))));
            var start          = follow.StartDocument?.Document;
            var where          = $"of the session {Session.Id} at {Session.LocationName}, {Session.EVSEId}";

            foreach (var (document, check) in checks)
            {

                var isStart  = ReferenceEquals(document, start);
                var what     = $"The signed {(isStart ? "start value" : "meter value")} {where}, {ReadingsInWords(document.Readings)},";

                switch (check.Holds)
                {

                    case true when isStart:
                        Log.Notice($"{what} holds: signed by its meter {check.MeterSerialNumber} with the key {check.KeyId} the proof of the station gave.", "charging", "meter", "poi");
                        break;

                    case true:
                        Log.Info  ($"{what} holds.", "charging", "meter", "poi");
                        break;

                    case false:
                        Log.Warning($"{what} is a wrong one: {check.Error} This phone stopped the session and follows its live link no more. " +
                                    (follow.StopLink is not null
                                         ? "It stops the station through the stop link of the web payment page of its operator."
                                         : "The web payment page of its operator gave no stop link: the driver stops the station there."),
                                    "charging", "meter", "poi");
                        break;

                    default:
                        Log.Warning($"{what} could not be checked: {check.Error}", "charging", "meter", "poi");
                        break;

                }

            }

            return checkedSession;

        }

        /// <summary>
        /// The readings of a signed document, for the log: "1000 kWh at
        /// 2026-11-09T10:00:00.0000000+00:00".
        /// </summary>
        private static String ReadingsInWords(IEnumerable<MeterReading> Readings)

            => String.Join(", ", Readings.Select(reading => (reading.EnergyKWh is Decimal energy
                                                                ? $"{energy.ToString(System.Globalization.CultureInfo.InvariantCulture)} kWh"
                                                                : "no energy register") + $" at {reading.MeasuredAt:o}"))
               is { Length: > 0 } words
                   ? words
                   : "no reading";

        #endregion

        #region (private) Follow(Id, Change)

        /// <summary>
        /// The given change made to the live link of a session, kept - or
        /// logged, where it could not be.
        /// </summary>
        private ChargingSession? Follow(String                                Id,
                                        Func<LiveLinkFollow, LiveLinkFollow>  Change)
        {

            if (Sessions.TryFollow(Id, Change, out var session, out var error))
                return session;

            Log.Warning($"What came over the live link of the session {Id} could not be kept: {error}", "charging");
            return null;

        }

        #endregion

        #region (private static) SignedTextsOf(Result)

        /// <summary>
        /// The signed documents of what ChargyCore made of an update, as they
        /// came: the OCMF documents of its readings.
        /// </summary>
        private static IReadOnlyList<String> SignedTextsOf(Object Result)

            => Result is ChargeTransparencyRecord record
                   ? [.. record.ChargingSessions.SelectMany(session     => session.Measurements).
                                                 SelectMany(measurement => measurement.Values).
                                                 OfType<OCMFMeasurementValue>().
                                                 Select    (value       => value.Document.Raw).
                                                 Where     (raw         => raw.Length > 0).
                                                 Distinct()]
                   : [];

        #endregion

        #region (private) StartFollowingLiveLinks() / StopFollowingLiveLinksAsync()

        /// <summary>
        /// Follow the live links of the sessions that are starting or charging,
        /// from now on for as long as the phone runs - those kept from before a
        /// restart as well, and those stopped for a wrong signed value whose
        /// station was not stopped yet through the stop link of the page.
        /// </summary>
        private void StartFollowingLiveLinks()
        {

            lock (liveLinksLock)
                followsLiveLinks = true;

            var now = TimeProvider.GetUtcNow();

            foreach (var session in Sessions.All().Where(session => session.LiveLink is LiveLinkFollow follow &&
                                                                    (session.IsActiveAt(now) || follow is { Refused: true, StopLink: not null, Stop: null })))
                StartFollowing(session);

        }

        private async Task StopFollowingLiveLinksAsync()
        {

            (CancellationTokenSource Stop, Task Following)[] followers;

            lock (liveLinksLock)
            {
                followsLiveLinks  = false;
                followers         = [.. liveLinkFollowers.Values];
            }

            foreach (var follower in followers)
            {
                try
                {
                    await follower.Stop.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // It ended on its own meanwhile.
                }
            }

            await Task.WhenAll(followers.Select(follower => follower.Following));

        }

        #endregion

    }

}
