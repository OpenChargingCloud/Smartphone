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

using System.Globalization;

using Newtonsoft.Json.Linq;

#endregion

namespace cloud.charging.open.Smartphone.Provider
{

    /// <summary>
    /// What the e-mobility provider said of a start through it, the last time
    /// the phone asked (<c>GET /api/v1/tickets/{id}</c>): its own state of the
    /// start, what the charge point operator answered the commands, and the
    /// session and the charge detail record the operator pushed for it - each
    /// as far as the operator sent it, and only where it is of this start.
    /// </summary>
    /// <remarks>
    /// The session and the CDR are kept as OCPI writes them and as they came,
    /// so that the signed meter values of a CDR are there to be checked once
    /// the phone checks them. Times in them stay text, with their offsets.
    /// </remarks>
    /// <param name="ReceivedAt">When the phone took it.</param>
    /// <param name="State">The provider's state of the start: "sent", "started", "refused", "failed", "stopped", "ended" or "expired".</param>
    /// <param name="EndedAt">Since when the provider blocks the start's token, or null while it is good.</param>
    /// <param name="Result">OCPI's CommandResult of START_SESSION, as the operator sent it, or null until it did.</param>
    /// <param name="StopResult">OCPI's CommandResult of STOP_SESSION, as the operator sent it, or null until it did.</param>
    /// <param name="Session">The OCPI session the operator pushed for the start, or null until it did.</param>
    /// <param name="CDR">The OCPI charge detail record the operator pushed for the start, or null until it did.</param>
    public sealed record ProviderReport(DateTimeOffset   ReceivedAt,
                                        String           State,
                                        DateTimeOffset?  EndedAt,
                                        JObject?         Result,
                                        JObject?         StopResult,
                                        JObject?         Session,
                                        JObject?         CDR)
    {

        #region Properties

        /// <summary>What the operator's CommandResult of the start says: ACCEPTED, EVSE_OCCUPIED, TIMEOUT, ... - or null.</summary>
        public String? ResultType
            => Result?.Value<String>("result");

        /// <summary>The status of the operator's session: PENDING, ACTIVE, COMPLETED, INVALID, RESERVATION - or null.</summary>
        public String? SessionStatus
            => Session?.Value<String>("status");

        /// <summary>What was charged, in kWh: the CDR's total energy, else the session's kWh - or null where neither says.</summary>
        public Decimal? EnergyKWh
            => Number(CDR?["total_energy"]) ?? Number(Session?["kwh"]);

        /// <summary>What it costs, with VAT where the operator says so: the CDR's, else the session's - or null where neither says.</summary>
        public Decimal? Cost
            => PriceOf(CDR?["total_cost"]) ?? PriceOf(Session?["total_cost"]);

        /// <summary>The currency the operator counts in - or null.</summary>
        public String? Currency
            => CDR?.Value<String>("currency") ?? Session?.Value<String>("currency");

        /// <summary>When charging began, as the operator says.</summary>
        public DateTimeOffset? ChargingSince
            => Time(Session?["start_date_time"]) ?? Time(CDR?["start_date_time"]);

        /// <summary>When charging ended, as the operator says.</summary>
        public DateTimeOffset? ChargingUntil
            => Time(CDR?["end_date_time"]) ?? Time(Session?["end_date_time"]);

        /// <summary>
        /// Whether nothing more is to come of the start: its CDR came, the start
        /// failed, or it is over without the operator ever reporting a session.
        /// </summary>
        public Boolean IsFinal

            => CDR is not null
            || State is "refused" or "failed"
            || SessionStatus is "INVALID"
            || Session is null && (State is "expired" or "ended" || ResultType is String result && result != "ACCEPTED");

        /// <summary>A few words of what it says, for the log.</summary>
        public String InWords

            => $"the start is {State}" +
               (ResultType    is String result  ? $", the operator answered it {result}"            : "") +
               (SessionStatus is String status  ? $", its session is {status}"                      : ", no session was reported") +
               (EnergyKWh     is Decimal energy ? $" with {energy.ToString("0.###", CultureInfo.InvariantCulture)} kWh" : "") +
               (CDR           is not null       ? ", its charge detail record came"                 : "");

        #endregion


        #region (static) Of(Answer, Start, Now, out Problem)

        /// <summary>
        /// The report of the given start in what the provider answered - null
        /// where the answer names no such start, or names it with another token
        /// or reference. A session or a CDR of another authorization reference,
        /// or for another token, is left out, and said so.
        /// </summary>
        /// <param name="Answer">The answer to <c>GET /api/v1/tickets/{id}</c>.</param>
        /// <param name="Start">What the phone keeps of the start.</param>
        /// <param name="Now">When it came.</param>
        /// <param name="Problem">What did not fit, or null.</param>
        public static ProviderReport? Of(JObject                    Answer,
                                         ChargingStartReference     Start,
                                         DateTimeOffset             Now,
                                         out String?                Problem)
        {

            Problem = null;

            var start = (Answer["starts"] as JArray ?? []).
                            OfType<JObject>().
                            FirstOrDefault(entry => String.Equals(entry.Value<String>("start"), Start.RequestDigest, StringComparison.OrdinalIgnoreCase));

            if (start is null)
            {
                Problem = $"It names no start {Start.RequestDigest[..16]}… of the ticket.";
                return null;
            }

            if (start.Value<String>("token") != Start.Token || start.Value<String>("authRef") != Start.AuthorizationReference)
            {
                Problem = $"It names the start with another token or reference than {Start.Token}, which it countersigned.";
                return null;
            }

            if (start.Value<String>("state") is not { Length: > 0 } state)
            {
                Problem = $"It says nothing of the state of {Start.Token}.";
                return null;
            }

            var problems  = new List<String>();
            var session   = OfThisStart(start["session"], "session",                 Start, problems);
            var cdr       = OfThisStart(start["cdr"],     "charge detail record",    Start, problems);

            Problem = problems.Count > 0 ? String.Join(" ", problems) : null;

            return new ProviderReport(Now,
                                      state,
                                      Time(start["endedAt"]),
                                      start["result"]     as JObject,
                                      start["stopResult"] as JObject,
                                      session,
                                      cdr);

        }

        /// <summary>
        /// A session or a CDR where it is of the start: its authorization
        /// reference the start's, its token the start's.
        /// </summary>
        private static JObject? OfThisStart(JToken?                 JSON,
                                            String                  What,
                                            ChargingStartReference  Start,
                                            List<String>            Problems)
        {

            if (JSON is not JObject json)
                return null;

            if (json.Value<String>("authorization_reference") != Start.AuthorizationReference ||
                json["cdr_token"]?.Value<String>("uid")       != Start.Token)
            {
                Problems.Add($"Its {What} '{json.Value<String>("id")}' is not of the start {Start.Token}, and was left out.");
                return null;
            }

            return json;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// What the pages are told of it.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("state",       State),
                           new JProperty("receivedAt",  ReceivedAt.ToString("o")),
                           new JProperty("cdr",         CDR is not null)
                       );

            if (ResultType is String result)
                json.Add(new JProperty("result",         result));

            if (SessionStatus is String status)
                json.Add(new JProperty("sessionStatus",  status));

            return json;

        }

        #endregion

        #region ToStorageJSON() / TryParseStorage(JSON)

        /// <summary>
        /// What the store keeps of it: all of it, as it came.
        /// </summary>
        public JObject ToStorageJSON()
        {

            var json = new JObject(
                           new JProperty("receivedAt",  ReceivedAt.ToString("o")),
                           new JProperty("state",       State)
                       );

            if (EndedAt is DateTimeOffset ended)
                json.Add(new JProperty("endedAt",     ended.ToString("o")));

            if (Result     is not null) json.Add(new JProperty("result",      Result));
            if (StopResult is not null) json.Add(new JProperty("stopResult",  StopResult));
            if (Session    is not null) json.Add(new JProperty("session",     Session));
            if (CDR        is not null) json.Add(new JProperty("cdr",         CDR));

            return json;

        }

        /// <summary>
        /// One report as the store kept it, or null where there is none.
        /// </summary>
        public static ProviderReport? TryParseStorage(JToken? JSON)
        {

            if (JSON is not JObject json ||
                json.Value<String>("state") is not { Length: > 0 } state ||
                Time(json["receivedAt"]) is not DateTimeOffset receivedAt)
                return null;

            return new ProviderReport(receivedAt,
                                      state,
                                      Time(json["endedAt"]),
                                      json["result"]     as JObject,
                                      json["stopResult"] as JObject,
                                      json["session"]    as JObject,
                                      json["cdr"]        as JObject);

        }

        #endregion


        #region (private static) Number(JSON) / PriceOf(JSON) / Time(JSON)

        private static Decimal? Number(JToken? JSON)

            => JSON is JValue { Type: JTokenType.Integer or JTokenType.Float } number
                   ? number.Value<Decimal>()
                   : null;

        /// <summary>
        /// An OCPI price: with VAT where it says so, else without - or, as OCPI
        /// 2.1.1 writes one, a number.
        /// </summary>
        private static Decimal? PriceOf(JToken? JSON)

            => JSON is JObject price
                   ? Number(price["incl_vat"]) ?? Number(price["excl_vat"])
                   : Number(JSON);

        /// <summary>
        /// A time as text, with its offset - or as Newtonsoft read one where it
        /// was not told to leave times as they are.
        /// </summary>
        private static DateTimeOffset? Time(JToken? JSON)

            => JSON switch {
                   JValue { Type: JTokenType.String } text when DateTimeOffset.TryParse(text.Value<String>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                       => time,
                   JValue { Type: JTokenType.Date, Value: DateTimeOffset offset }
                       => offset,
                   JValue { Type: JTokenType.Date, Value: DateTime time }
                       => new DateTimeOffset(DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : time.Kind)),
                   _   => null
               };

        #endregion

    }

}
