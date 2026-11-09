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


        #region FollowLiveLinkAsync(LocationId, EVSEUid, LiveLink, Who, CancellationToken = default)

        /// <summary>
        /// Take over the live link of a charging session that the web payment
        /// page of the operator of the station started - paid there, without an
        /// e-mobility provider - and follow it. Only at an EVSE of a station of
        /// a roaming network whose proof was checked and still holds: the
        /// session keeps its evidence and its price, as one this phone starts.
        /// </summary>
        /// <param name="LocationId">The station, by the id the map gave it.</param>
        /// <param name="EVSEUid">Its EVSE, by the uid the map gave it.</param>
        /// <param name="LiveLink">The live link the page gave: the https URL of its document, or the document itself, as text or JSON.</param>
        /// <param name="Who">Who handed it over, for the log and the session.</param>
        /// <param name="CancellationToken">To stop fetching its document.</param>
        public async Task<ChargingStart> FollowLiveLinkAsync(String             LocationId,
                                                             String             EVSEUid,
                                                             JToken             LiveLink,
                                                             String             Who,
                                                             CancellationToken  CancellationToken = default)
        {

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

            var follow = new LiveLinkFollow(document, from, TimeProvider.GetUtcNow());

            if (follow.EVSEId is String named && named != evse.EVSEId)
                return ChargingStart.Refused(ChargingRefusal.Conflict, $"The live link is of {named}, not of {evse.EVSEId} at {location.Name}: it was handed over at the wrong charge point.");

            if (!Sessions.TryStart(location with { Tariff = tariff }, evse, (one, _) => RoamingStatusOf(one), Who, null, null,
                                   out var session, out var error, out var notSaved, EvidenceOf(station, evse), follow))
                return ChargingStart.Refused(notSaved ? ChargingRefusal.NotSaved : ChargingRefusal.Conflict, error);

            Log.Notice($"'{Who}' handed over the live link of a session at {location.Name}, {evse.EVSEId}, which the web payment page of its operator started" +
                       (from is not null ? $", from {from}" : "") +
                       $", for {session.PricePerKWh:0.00##} {session.Currency} per kWh, as its checked proof says. The session is {session.Id}.",
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

                    if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                        !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
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
        /// start value checked once it comes - a wrong one stops the session -
        /// and ended once the meter signs the end of the transaction or the
        /// station ends the stream. Where none of its endpoints answers, asked
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

                    if (session?.LiveLink is not LiveLinkFollow follow || !session.IsActiveAt(TimeProvider.GetUtcNow()))
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

                            var followed = CheckStart(Follow(Id, kept => kept.WithUpdate(TimeProvider.GetUtcNow(),
                                                                                         update.Transport.AsText(),
                                                                                         update.Endpoint,
                                                                                         update.Result.GetType().Name,
                                                                                         SignedTextsOf(update.Result))));

                            if (followed?.LiveLink?.StartRefused == true)
                                return;

                            if (followed?.LiveLink?.EndedAt is not null)
                            {
                                Log.Notice($"The session {Id} at {followed.LocationName}, {followed.EVSEId}, ended as its live link says: {followed.LiveLink.EndedBecause}", "charging");
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

                        Log.Notice($"The session {Id} at {ended?.LocationName}, {ended?.EVSEId}, ended as its live link says: {ended?.LiveLink?.EndedBecause}", "charging");
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

        #region (private) CheckStart(Session)

        /// <summary>
        /// The signed start value of the given session, checked once it came
        /// over its live link (<see cref="SessionCheck.StartOf"/>) and kept with
        /// it - checked again with every update while it could not be decided.
        /// A wrong one stops the session: it is said in the log, and its live
        /// link is followed no more.
        /// </summary>
        /// <remarks>
        /// The web payment page of the operator gives this phone no link to
        /// stop the station with: the driver stops it at the page.
        /// </remarks>
        private ChargingSession? CheckStart(ChargingSession? Session)
        {

            if (Session?.LiveLink is not LiveLinkFollow follow || follow.Start?.Holds is not null)
                return Session;

            if (SessionCheck.StartOf(RoamingNetwork, Session, TimeProvider.GetUtcNow()) is not LiveLinkStartCheck check)
                return Session;

            // Not decided again, for the same reason: not kept again.
            if (check.Holds is null && follow.Start is LiveLinkStartCheck last && last.Error == check.Error && last.Text == check.Text)
                return Session;

            var checkedSession = Follow(Session.Id, kept => kept.WithStart(check));

            var where = $"of the session {Session.Id} at {Session.LocationName}, {Session.EVSEId}";
            var start = check.Reading is MeterReading reading
                            ? (reading.EnergyKWh is Decimal energy
                                   ? $"{energy.ToString(System.Globalization.CultureInfo.InvariantCulture)} kWh"
                                   : "no energy register") + $" at {reading.MeasuredAt:o}"
                            : "no reading";

            switch (check.Holds)
            {

                case true:
                    Log.Notice($"The signed start value {where}, {start}, holds: signed by its meter {check.MeterSerialNumber} with the key {check.KeyId} the proof of the station gave.",
                               "charging", "meter", "poi");
                    break;

                case false:
                    Log.Warning($"The signed start value {where}, {start}, is a wrong one: {check.Error} This phone stopped the session and follows its live link no more. " +
                                "The web payment page of its operator gives no link to stop the station with: the driver stops it there.",
                                "charging", "meter", "poi");
                    break;

                default:
                    Log.Warning($"The signed start value {where}, {start}, could not be checked: {check.Error}", "charging", "meter", "poi");
                    break;

            }

            return checkedSession;

        }

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
        /// restart as well.
        /// </summary>
        private void StartFollowingLiveLinks()
        {

            lock (liveLinksLock)
                followsLiveLinks = true;

            var now = TimeProvider.GetUtcNow();

            foreach (var session in Sessions.All().Where(session => session.LiveLink is not null && session.IsActiveAt(now)))
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
