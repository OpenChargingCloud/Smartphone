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
    /// One signed document a live link brought, as it came: an OCMF document
    /// "OCMF|{payload}|{signature}", when it came, and over which transport
    /// from where.
    /// </summary>
    public sealed record LiveLinkDocument(DateTimeOffset  ReceivedAt,
                                          String          Transport,
                                          String          Endpoint,
                                          String          Text)
    {

        public JObject ToJSON()

            => new (new JProperty("receivedAt",  ReceivedAt.ToString("o")),
                    new JProperty("transport",   Transport),
                    new JProperty("endpoint",    Endpoint),
                    new JProperty("text",        Text));

        public static LiveLinkDocument? TryParse(JToken? JSON)

            => JSON is JObject json                                                                   &&
               json.Value<String>("receivedAt") is String receivedAt                                    &&
               DateTimeOffset.TryParse(receivedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) &&
               json.Value<String>("text") is { Length: > 0 } text
                   ? new LiveLinkDocument(at, json.Value<String>("transport") ?? "", json.Value<String>("endpoint") ?? "", text)
                   : null;

    }


    /// <summary>
    /// The live link of a charging session that the web payment page of the
    /// operator of its station started - not this phone - as the driver handed
    /// it over, and what following it brought so far: how often something came,
    /// when last, over which transport from where, what ChargyCore made of it,
    /// and the signed documents among it, each kept as it came.
    /// </summary>
    /// <remarks>
    /// Nothing of it is checked here against the proof of the station: the
    /// readings are what the documents say, and the keys a live link names are
    /// no authority. The documents are kept so that they can be checked with
    /// the key the proofs of the station give for its meter.
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
        /// How many signed documents are kept at most: the first, which holds
        /// the start, and the latest ones.
        /// </summary>
        public const Int32 MaxDocuments = 1000;

        /// <summary>
        /// The parts of a transaction of an OCMF reading ("TX") that end it:
        /// its end, ended at the station, ended remotely, aborted, the power
        /// lost.
        /// </summary>
        private static readonly HashSet<String> endingTransactions = [ "E", "L", "R", "A", "P" ];

        /// <summary>
        /// The readings of the documents they were read of: read once for every
        /// list of documents - a copy made with other documents reads anew.
        /// </summary>
        private sealed record ReadingsOfDocuments(IReadOnlyList<LiveLinkDocument>? Documents, IReadOnlyList<MeterReading> Readings);

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
        /// meter measured them - as the documents say, not checked.
        /// </summary>
        public IReadOnlyList<MeterReading> Readings
        {
            get
            {

                if (readings is ReadingsOfDocuments read && ReferenceEquals(read.Documents, Documents))
                    return read.Readings;

                var fresh = new ReadingsOfDocuments(Documents,
                                                    [.. Signed.SelectMany(document => ReadingsOf(document.Text)).
                                                               DistinctBy(reading  => (reading.MeasuredAt, reading.EnergyKWh, reading.Transaction)).
                                                               OrderBy   (reading  => reading.MeasuredAt)]);

                readings = fresh;

                return fresh.Readings;

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

            var documents = Signed.ToList();

            foreach (var text in Texts)
                if (!documents.Any(document => document.Text == text))
                    documents.Add(new LiveLinkDocument(At, Transport, Endpoint, text));

            // The first holds the start: what goes is the oldest after it.
            while (documents.Count > MaxDocuments)
                documents.RemoveAt(1);

            var updated = this with {
                              Updates        = Updates + 1,
                              LastUpdateAt   = At,
                              LastTransport  = Transport,
                              LastEndpoint   = Endpoint,
                              LastKind       = Kind,
                              Problem        = null,
                              Documents      = documents
                          };

            return EndedAt is null && updated.Readings.FirstOrDefault(reading => reading.Transaction is String transaction && endingTransactions.Contains(transaction)) is MeterReading end
                       ? updated with { EndedAt = At, EndedBecause = $"The meter signed the end of the transaction (\"{end.Transaction}\") at {end.MeasuredAt:o}." }
                       : updated;

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
        /// over it, and the readings its signed documents say - not checked.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("handedOverAt",   HandedOverAt.ToString("o")),
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
                    new JProperty("readings",       new JArray(Readings.Select(reading => reading.ToJSON()))),
                    new JProperty("energyKWh",      EnergyKWh),
                    new JProperty("checked",        false));

        #endregion

        #region ToStorageJSON() / TryParse(JSON)

        /// <summary>
        /// What the store keeps of it: all of it, its documents as they came.
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
        /// A live link as the store kept it, or null where it kept none.
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
                                      [.. (json["documents"] as JArray ?? []).Select(LiveLinkDocument.TryParse).OfType<LiveLinkDocument>()]);

        }

        #endregion

    }

}
