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

using cloud.charging.open.chargy.Formats.OCMF;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// The check of one signed document a live link brought, made when it
    /// came (<see cref="SessionCheck.ValuesOf"/>): whether it holds - true,
    /// false where it is a wrong meter value, null where this phone could not
    /// decide then - and why not, and the meter and its key it was checked
    /// with.
    /// </summary>
    /// <param name="CheckedAt">When it was checked.</param>
    /// <param name="Holds">Whether it holds; null where it could not be checked.</param>
    /// <param name="Error">Why it does not hold, or was not checked.</param>
    /// <param name="MeterSerialNumber">The meter, as the document names it.</param>
    /// <param name="MeterId">The meter, as the proof names it, where its key was found.</param>
    /// <param name="KeyId">The key it was checked with.</param>
    /// <param name="Result">What ChargyCore said of its signature, where it was asked.</param>
    public sealed record LiveLinkValueCheck(DateTimeOffset  CheckedAt,
                                            Boolean?        Holds,
                                            String?         Error,
                                            String?         MeterSerialNumber  = null,
                                            String?         MeterId            = null,
                                            String?         KeyId              = null,
                                            String?         Result             = null)
    {

        public JObject ToJSON()

            => new (new JProperty("checkedAt",          CheckedAt.ToString("o")),
                    new JProperty("holds",              Holds),
                    new JProperty("error",              Error),
                    new JProperty("meterSerialNumber",  MeterSerialNumber),
                    new JProperty("meterId",            MeterId),
                    new JProperty("keyId",              KeyId),
                    new JProperty("result",             Result));

        /// <summary>
        /// A check as the store kept it, or null where it kept none.
        /// </summary>
        public static LiveLinkValueCheck? TryParse(JToken? JSON)

            => JSON is JObject json                                                                                 &&
               json.Value<String>("checkedAt") is String checkedAt                                                    &&
               DateTimeOffset.TryParse(checkedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                   ? new LiveLinkValueCheck(at,
                                            json["holds"]?.Type == JTokenType.Boolean ? json.Value<Boolean>("holds") : null,
                                            json.Value<String>("error"),
                                            json.Value<String>("meterSerialNumber"),
                                            json.Value<String>("meterId"),
                                            json.Value<String>("keyId"),
                                            json.Value<String>("result"))
                   : null;

    }


    /// <summary>
    /// One signed document a live link brought, as it came: an OCMF document
    /// "OCMF|{payload}|{signature}", when it came, and over which transport
    /// from where - and how it held, once it was checked.
    /// </summary>
    public sealed record LiveLinkDocument(DateTimeOffset       ReceivedAt,
                                          String               Transport,
                                          String               Endpoint,
                                          String               Text,
                                          LiveLinkValueCheck?  Check  = null)
    {

        #region Data

        /// <summary>
        /// The parts of a transaction of an OCMF reading ("TX") that end it:
        /// its end, ended at the station, ended remotely, aborted, the power
        /// lost.
        /// </summary>
        internal static readonly HashSet<String> EndingTransactions = [ "E", "L", "R", "A", "P" ];

        /// <summary>
        /// Its readings, read once of the text they were read of.
        /// </summary>
        private (String Text, IReadOnlyList<MeterReading> Readings)? read;

        #endregion

        #region Properties

        /// <summary>
        /// Its readings, as it says them.
        /// </summary>
        public IReadOnlyList<MeterReading> Readings
        {
            get
            {

                if (read is (String text, IReadOnlyList<MeterReading> readings) && ReferenceEquals(text, Text))
                    return readings;

                var fresh = LiveLinkFollow.ReadingsOf(Text);

                read = (Text, fresh);

                return fresh;

            }
        }

        /// <summary>
        /// Its reading of the begin of the transaction ("B"), where it holds one.
        /// </summary>
        public MeterReading? Begin
            => Readings.FirstOrDefault(reading => reading.Transaction == "B");

        /// <summary>
        /// Whether one of its readings ends the transaction.
        /// </summary>
        public Boolean Ends
            => Readings.Any(reading => reading.Transaction is String transaction && EndingTransactions.Contains(transaction));

        #endregion


        #region ToJSON() / TryParse(JSON)

        /// <summary>
        /// What the store keeps of it: all of it, its text as it came.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(new JProperty("receivedAt",  ReceivedAt.ToString("o")),
                                   new JProperty("transport",   Transport),
                                   new JProperty("endpoint",    Endpoint),
                                   new JProperty("text",        Text));

            if (Check is not null)
                json.Add(new JProperty("check", Check.ToJSON()));

            return json;

        }

        public static LiveLinkDocument? TryParse(JToken? JSON)

            => JSON is JObject json                                                                   &&
               json.Value<String>("receivedAt") is String receivedAt                                    &&
               DateTimeOffset.TryParse(receivedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) &&
               json.Value<String>("text") is { Length: > 0 } text
                   ? new LiveLinkDocument(at,
                                          json.Value<String>("transport") ?? "",
                                          json.Value<String>("endpoint")  ?? "",
                                          text,
                                          LiveLinkValueCheck.TryParse(json["check"]))
                   : null;

        #endregion

    }


    /// <summary>
    /// The live link of a charging session that the web payment page of the
    /// operator of its station started - not this phone - as the driver handed
    /// it over, and what following it brought so far: how often something came,
    /// when last, over which transport from where, what ChargyCore made of it,
    /// and the signed documents among it, each kept as it came, with how it
    /// held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys a live link names are no authority: every signed document it
    /// brings is checked as it comes, with the key the proofs of the station
    /// give for its meter, and against the values that held before it - its
    /// start value first (<see cref="Start"/>). A wrong one stops the session
    /// here (<see cref="Wrong"/>). The end value is not checked yet.
    /// </para>
    /// </remarks>
    /// <param name="Link">The live link, its JSON document as it was handed over or fetched.</param>
    /// <param name="From">The URL it was fetched from, or null where the document itself was handed over.</param>
    /// <param name="HandedOverAt">When it was handed over.</param>
    /// <param name="Updates">How often something came over it.</param>
    /// <param name="LastUpdateAt">When something came last.</param>
    /// <param name="LastTransport">Over which transport.</param>
    /// <param name="LastEndpoint">From where.</param>
    /// <param name="LastKind">What ChargyCore made of it: the type it read it as.</param>
    /// <param name="Problem">Why nothing comes now, where nothing does.</param>
    /// <param name="EndedAt">When it ended: the meter signed the end of the transaction, or the station ended the stream.</param>
    /// <param name="EndedBecause">Why it is taken as ended.</param>
    /// <param name="Documents">The signed documents it brought, each once, in the order they came.</param>
    public sealed record LiveLinkFollow(JObject                           Link,
                                        String?                           From,
                                        DateTimeOffset                    HandedOverAt,
                                        Int32                             Updates        = 0,
                                        DateTimeOffset?                   LastUpdateAt   = null,
                                        String?                           LastTransport  = null,
                                        String?                           LastEndpoint   = null,
                                        String?                           LastKind       = null,
                                        String?                           Problem        = null,
                                        DateTimeOffset?                   EndedAt        = null,
                                        String?                           EndedBecause   = null,
                                        IReadOnlyList<LiveLinkDocument>?  Documents      = null)
    {

        #region Data

        /// <summary>
        /// How many signed documents are kept at most: the first, the one that
        /// holds the start, and the latest ones.
        /// </summary>
        public const Int32 MaxDocuments = 1000;

        /// <summary>
        /// The readings of the documents they were read of, whether each held,
        /// and the first document with the begin of the transaction among
        /// them: read once for every list of documents - a copy made with other
        /// documents reads anew.
        /// </summary>
        private sealed record ReadingsOfDocuments(IReadOnlyList<LiveLinkDocument>?                       Documents,
                                                  IReadOnlyList<(MeterReading Reading, Boolean? Holds)>  Checked,
                                                  IReadOnlyList<MeterReading>                            Readings,
                                                  (LiveLinkDocument Document, MeterReading Reading)?     Start);

        private ReadingsOfDocuments? readings;

        #endregion

        #region Properties

        /// <summary>
        /// The signed documents it brought, each once, in the order they came.
        /// </summary>
        public IReadOnlyList<LiveLinkDocument> Signed
            => Documents ?? [];

        /// <summary>
        /// The EVSE the live link names, where it names one: its charging
        /// station's "EVSE", by its "@id".
        /// </summary>
        public String? EVSEId
            => Link["chargingStation"]?["EVSE"]?["@id"] is JValue { Type: JTokenType.String } id
                   ? id.Value<String>()
                   : null;

        /// <summary>
        /// The readings of its signed documents, each once, in the order the
        /// meter measured them - as the documents say.
        /// </summary>
        public IReadOnlyList<MeterReading> Readings
            => Read().Readings;

        /// <summary>
        /// The signed document of its start, of those it brought: the first
        /// that came with a reading of the begin of the transaction ("B"),
        /// and that reading - or null while none came.
        /// </summary>
        public (LiveLinkDocument Document, MeterReading Reading)? StartDocument
            => Read().Start;

        /// <summary>
        /// How its signed start value held, once it was checked.
        /// </summary>
        public LiveLinkValueCheck? Start
            => StartDocument?.Document.Check;

        /// <summary>
        /// The first signed document it brought that is a wrong meter value:
        /// this phone stopped the session - or null.
        /// </summary>
        public LiveLinkDocument? Wrong
            => Signed.FirstOrDefault(document => document.Check?.Holds == false);

        /// <summary>
        /// Whether one of its signed values is a wrong one: this phone stopped
        /// the session.
        /// </summary>
        public Boolean Refused
            => Wrong is not null;

        /// <summary>
        /// Why this phone stopped the session, where it did.
        /// </summary>
        public String? RefusedBecause
            => Wrong is LiveLinkDocument wrong
                   ? ReferenceEquals(wrong, StartDocument?.Document)
                         ? $"Its signed start meter value is a wrong one: {wrong.Check!.Error}"
                         : $"A signed meter value after its start is a wrong one: {wrong.Check!.Error}"
                   : null;

        /// <summary>
        /// The signed documents to check now: those not checked, or not
        /// decided, but those that end the transaction - the end value, not
        /// checked here - unless one holds the start.
        /// </summary>
        public IReadOnlyList<LiveLinkDocument> ToCheck
        {
            get
            {

                var start = StartDocument?.Document;

                return [.. Signed.Where(document => document.Check?.Holds is null &&
                                                    document.Readings.Count > 0     &&
                                                    (!document.Ends || ReferenceEquals(document, start)))];

            }
        }

        /// <summary>
        /// What was charged as its readings say: the energy register of the
        /// latest reading less that of the first - or null while there are not
        /// two readings with an energy register.
        /// </summary>
        public Decimal? EnergyKWh
        {
            get
            {

                var withEnergy = Readings.Where(reading => reading.EnergyKWh is not null).ToArray();

                return withEnergy.Length >= 2
                           ? Math.Max(0m, withEnergy[^1].EnergyKWh!.Value - withEnergy[0].EnergyKWh!.Value)
                           : withEnergy.Length == 1 ? 0m : null;

            }
        }

        private ReadingsOfDocuments Read()
        {

            if (readings is ReadingsOfDocuments read && ReferenceEquals(read.Documents, Documents))
                return read;

            var all    = new Dictionary<(DateTimeOffset, Decimal?, String?), (MeterReading Reading, Boolean? Holds)>();
            (LiveLinkDocument, MeterReading)? start = null;

            foreach (var document in Signed)
            {

                foreach (var reading in document.Readings)
                {

                    var key = (reading.MeasuredAt, reading.EnergyKWh, reading.Transaction);

                    // Of a reading in more than one document: how the first
                    // that was decided held.
                    if (!all.TryGetValue(key, out var known) || (known.Holds is null && document.Check?.Holds is not null))
                        all[key] = (known.Reading ?? reading, document.Check?.Holds);

                }

                if (start is null && document.Begin is MeterReading begin)
                    start = (document, begin);

            }

            IReadOnlyList<(MeterReading Reading, Boolean? Holds)> ordered = [.. all.Values.OrderBy(reading => reading.Reading.MeasuredAt)];

            var fresh = new ReadingsOfDocuments(Documents,
                                                ordered,
                                                [.. ordered.Select(reading => reading.Reading)],
                                                start);

            readings = fresh;

            return fresh;

        }

        #endregion


        #region WithUpdate(At, Transport, Endpoint, Kind, Texts)

        /// <summary>
        /// Something came over it: counted, and its signed documents kept that
        /// were not kept yet - ended, where one of their readings ends the
        /// transaction.
        /// </summary>
        public LiveLinkFollow WithUpdate(DateTimeOffset       At,
                                         String               Transport,
                                         String               Endpoint,
                                         String               Kind,
                                         IEnumerable<String>  Texts)
        {

            var documents  = Signed.ToList();
            var start      = StartDocument?.Document;

            foreach (var text in Texts)
                if (!documents.Any(document => document.Text == text))
                    documents.Add(new LiveLinkDocument(At, Transport, Endpoint, text));

            // The first and the one of the start stay: what goes is the oldest
            // after the first that is not the one of the start.
            while (documents.Count > MaxDocuments)
                documents.RemoveAt(documents.Count > 2 && ReferenceEquals(documents[1], start) ? 2 : 1);

            var updated = this with {
                              Updates        = Updates + 1,
                              LastUpdateAt   = At,
                              LastTransport  = Transport,
                              LastEndpoint   = Endpoint,
                              LastKind       = Kind,
                              Problem        = null,
                              Documents      = documents
                          };

            return EndedAt is null && updated.Readings.FirstOrDefault(reading => reading.Transaction is String transaction && LiveLinkDocument.EndingTransactions.Contains(transaction)) is MeterReading end
                       ? updated with { EndedAt = At, EndedBecause = $"The meter signed the end of the transaction (\"{end.Transaction}\") at {end.MeasuredAt:o}." }
                       : updated;

        }

        #endregion

        #region WithChecks(Checks)

        /// <summary>
        /// How its signed documents held, as checked now - those not decided
        /// before: a document that held or did not stays as it was.
        /// </summary>
        public LiveLinkFollow WithChecks(IEnumerable<(String Text, LiveLinkValueCheck Check)> Checks)
        {

            var checks = Checks.ToDictionary(check => check.Text, check => check.Check);

            return checks.Count == 0
                       ? this
                       : this with {
                             Documents = [.. Signed.Select(document => document.Check?.Holds is null && checks.TryGetValue(document.Text, out var check)
                                                                           ? document with { Check = check }
                                                                           : document)]
                         };

        }

        #endregion

        #region ReadingsOf(Text)

        /// <summary>
        /// The readings of an OCMF document, or none where it is none.
        /// </summary>
        public static IReadOnlyList<MeterReading> ReadingsOf(String Text)
        {

            try
            {

                var scanned = new OCMFDocumentScanner().Scan([ Text ]);

                return scanned.Documents.Count == 1 &&
                       SessionCheck.TryReadingsOf(scanned.Documents[0].Payload["RD"], out var readings, out _)
                           ? readings
                           : [];

            }
            catch (Exception)
            {
                return [];
            }

        }

        #endregion


        #region ToJSON()

        /// <summary>
        /// The live link as the pages show it: where it came from, what came
        /// over it, the readings its signed documents say and whether each
        /// held, how its signed start value held - null while none came -, how
        /// many of the values after it held, and the wrong one, where one is.
        /// </summary>
        public JObject ToJSON()
        {

            var start  = StartDocument;
            var after  = Signed.Where(document => !ReferenceEquals(document, start?.Document)).ToArray();
            var wrong  = Wrong is LiveLinkDocument refused && !ReferenceEquals(refused, start?.Document) ? refused : null;

            JObject CheckJSON(LiveLinkValueCheck Check, String Name, JToken Readings)
            {
                var json = Check.ToJSON();
                json.Add(new JProperty(Name, Readings));
                return json;
            }

            return new (new JProperty("handedOverAt",   HandedOverAt.ToString("o")),
                        new JProperty("from",           From),
                        new JProperty("evseId",         EVSEId),
                        new JProperty("transports",     new JArray((Link["liveTransports"] as JArray ?? []).
                                                                       OfType<JObject>().
                                                                       Select(transport => transport.Value<String>("type")).
                                                                       OfType<String>())),
                        new JProperty("updates",        Updates),
                        new JProperty("lastUpdateAt",   LastUpdateAt?.ToString("o")),
                        new JProperty("lastTransport",  LastTransport),
                        new JProperty("lastEndpoint",   LastEndpoint),
                        new JProperty("lastKind",       LastKind),
                        new JProperty("problem",        Problem),
                        new JProperty("endedAt",        EndedAt?.ToString("o")),
                        new JProperty("endedBecause",   EndedBecause),
                        new JProperty("documents",      Signed.Count),
                        new JProperty("readings",       new JArray(Read().Checked.Select(reading => {
                                                            var json = reading.Reading.ToJSON();
                                                            json.Add(new JProperty("holds", reading.Holds));
                                                            return json;
                                                        }))),
                        new JProperty("energyKWh",      EnergyKWh),
                        new JProperty("start",          start?.Document.Check is LiveLinkValueCheck startCheck
                                                            ? CheckJSON(startCheck, "reading", start.Value.Reading.ToJSON())
                                                            : null),
                        new JProperty("values",         new JObject(
                                                            new JProperty("holding",     after.Count(document => document.Check?.Holds == true)),
                                                            new JProperty("wrong",       after.Count(document => document.Check?.Holds == false)),
                                                            new JProperty("undecided",   after.Count(document => document.Check is not null && document.Check.Holds is null)),
                                                            new JProperty("notChecked",  after.Count(document => document.Check is null))
                                                        )),
                        new JProperty("wrong",          wrong?.Check is LiveLinkValueCheck wrongCheck
                                                            ? CheckJSON(wrongCheck, "readings", new JArray(wrong.Readings.Select(reading => reading.ToJSON())))
                                                            : null));

        }

        #endregion

        #region ToStorageJSON() / TryParse(JSON)

        /// <summary>
        /// What the store keeps of it: all of it, its documents as they came,
        /// each with how it held.
        /// </summary>
        public JObject ToStorageJSON()
        {

            var json = new JObject(
                           new JProperty("link",          Link.DeepClone()),
                           new JProperty("handedOverAt",  HandedOverAt.ToString("o")),
                           new JProperty("updates",       Updates)
                       );

            if (From          is not null)  json.Add(new JProperty("from",           From));
            if (LastUpdateAt  is not null)  json.Add(new JProperty("lastUpdateAt",   LastUpdateAt.Value.ToString("o")));
            if (LastTransport is not null)  json.Add(new JProperty("lastTransport",  LastTransport));
            if (LastEndpoint  is not null)  json.Add(new JProperty("lastEndpoint",   LastEndpoint));
            if (LastKind      is not null)  json.Add(new JProperty("lastKind",       LastKind));
            if (Problem       is not null)  json.Add(new JProperty("problem",        Problem));
            if (EndedAt       is not null)  json.Add(new JProperty("endedAt",        EndedAt.Value.ToString("o")));
            if (EndedBecause  is not null)  json.Add(new JProperty("endedBecause",   EndedBecause));

            json.Add(new JProperty("documents", new JArray(Signed.Select(document => document.ToJSON()))));

            return json;

        }

        /// <summary>
        /// A live link as the store kept it, or null where it kept none - the
        /// check of its start, as a store kept it beside its documents before
        /// each kept its own, with the document of the start.
        /// </summary>
        public static LiveLinkFollow? TryParse(JToken? JSON)
        {

            if (JSON is not JObject json || json["link"] is not JObject link)
                return null;

            static DateTimeOffset? Time(JToken? Token)
                => Token?.Type == JTokenType.String && DateTimeOffset.TryParse(Token.Value<String>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                       ? time
                       : null;

            if (Time(json["handedOverAt"]) is not DateTimeOffset handedOverAt)
                throw new FormatException("Its live link says not when it was handed over.");

            IReadOnlyList<LiveLinkDocument> documents = [.. (json["documents"] as JArray ?? []).Select(LiveLinkDocument.TryParse).OfType<LiveLinkDocument>()];

            if (json["start"] is JObject start && start.Value<String>("text") is String startText && LiveLinkValueCheck.TryParse(start) is LiveLinkValueCheck startCheck)
                documents = [.. documents.Select(document => document.Text == startText && document.Check is null ? document with { Check = startCheck } : document)];

            return new LiveLinkFollow((JObject) link.DeepClone(),
                                      json.Value<String>("from"),
                                      handedOverAt,
                                      json["updates"]?.Type == JTokenType.Integer ? json.Value<Int32>("updates") : 0,
                                      Time(json["lastUpdateAt"]),
                                      json.Value<String>("lastTransport"),
                                      json.Value<String>("lastEndpoint"),
                                      json.Value<String>("lastKind"),
                                      json.Value<String>("problem"),
                                      Time(json["endedAt"]),
                                      json.Value<String>("endedBecause"),
                                      documents);

        }

        #endregion

    }

}
