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
using System.Text;

using Newtonsoft.Json.Linq;

using cloud.charging.open.chargy.Formats.OCMF;
using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.Smartphone.Roaming;

using ChargyDictionary  = cloud.charging.open.chargy.I18NDictionary;
using ChargyResult      = cloud.charging.open.chargy.VerificationResult;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// One reading of a signed meter value: when it was measured, as the meter
    /// signed it, the energy register then, in kWh, which part of the
    /// transaction it is - "B" its begin, "E" its end - and its status.
    /// </summary>
    public sealed record MeterReading(DateTimeOffset  MeasuredAt,
                                      Decimal?        EnergyKWh,
                                      String?         Transaction,
                                      String?         Status)
    {

        public JObject ToJSON()

            => new (new JProperty("measuredAt",   MeasuredAt.ToString("o")),
                    new JProperty("energyKWh",    EnergyKWh),
                    new JProperty("transaction",  Transaction),
                    new JProperty("status",       Status));

    }


    /// <summary>
    /// One signed meter value of a charge detail record, checked: an OCMF
    /// document, signed by the key the proofs kept of the session's station
    /// give for the energy meter of its EVSE at the time it was measured - or
    /// why not.
    /// </summary>
    /// <param name="Nature">What the record calls it: "Start", "End", "Intermediate".</param>
    /// <param name="Verified">Whether its signature is the meter's.</param>
    /// <param name="Error">Why not.</param>
    /// <param name="Result">What ChargyCore said of its signature, where it was asked.</param>
    /// <param name="MeterSerialNumber">The meter, as the document names it.</param>
    /// <param name="MeterId">The meter, as the proof names it, where its key was found.</param>
    /// <param name="KeyId">The key it was checked with.</param>
    /// <param name="Identification">What the document says identified the charging (its "ID").</param>
    /// <param name="OfThisStart">Whether that is the token or the ticket of the session's start.</param>
    /// <param name="Readings">Its readings.</param>
    public sealed record MeterValueCheck(String?                      Nature,
                                         Boolean                      Verified,
                                         String?                      Error,
                                         String?                      Result,
                                         String?                      MeterSerialNumber,
                                         String?                      MeterId,
                                         String?                      KeyId,
                                         String?                      Identification,
                                         Boolean                      OfThisStart,
                                         IReadOnlyList<MeterReading>  Readings)
    {

        public JObject ToJSON()

            => new (new JProperty("nature",             Nature),
                    new JProperty("verified",           Verified),
                    new JProperty("error",              Error),
                    new JProperty("result",             Result),
                    new JProperty("meterSerialNumber",  MeterSerialNumber),
                    new JProperty("meterId",            MeterId),
                    new JProperty("keyId",              KeyId),
                    new JProperty("identification",     Identification),
                    new JProperty("ofThisStart",        OfThisStart),
                    new JProperty("readings",           new JArray(Readings.Select(reading => reading.ToJSON()))));

    }


    /// <summary>
    /// The price of a session, checked: the price statement of the operator
    /// of its station in force when it started, of those kept with it, as
    /// WWCP_POI's SessionPriceEvidence establishes it - or why not.
    /// </summary>
    /// <param name="Verification">What was established, each apart.</param>
    /// <param name="Error">Why nothing was.</param>
    /// <param name="PricePerKWh">The price per kWh of the statement selected.</param>
    public sealed record SessionPriceCheck(SessionPriceVerification?  Verification,
                                           String?                    Error,
                                           Decimal?                   PricePerKWh);


    /// <summary>
    /// What a charging session at a station of a roaming network is checked
    /// against, at one time: the signed meter values of its charge detail
    /// record, each with the key of the energy meter the proofs kept of its
    /// station give for when it was measured, and its price, against the
    /// price statements that held when it started - both under the identity
    /// keys trusted here now, not those trusted when it started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A key a charge detail record names itself is no authority: the key is
    /// the one the roaming network operator attested for the meter of the
    /// EVSE. The times are the ones the meter signed; that the meter's clock
    /// was right, and that the version of the proof was the latest one, is
    /// not proven (WWCP_POI's HistoricalMeterVerification).
    /// </para>
    /// <para>
    /// The price is the one the operator of the station stated for its EVSE,
    /// its station or its pool, signed and in force at the start. That the
    /// operator assigned it to this very session is not proven where it signed
    /// no assignment - "unbound" - and it is compared with the price the
    /// phone counted with, and with what the record says it costs.
    /// </para>
    /// </remarks>
    public sealed class SessionCheck
    {

        #region Data

        private static readonly Lazy<ChargyDictionary> chargyDictionary = new (() => ChargyDictionary.Default());

        /// <summary>
        /// The DER encoding of a SubjectPublicKeyInfo of an ECDSA P-256 key up
        /// to its point: what an energy meter's key is given to ChargyCore as.
        /// </summary>
        private const String P256SPKIPrefix = "3059301306072a8648ce3d020106082a8648ce3d030107034200";

        #endregion

        #region Properties

        /// <summary>
        /// When it was checked.
        /// </summary>
        public DateTimeOffset                  CheckedAt          { get; }

        /// <summary>
        /// Why nothing was checked: the identity keys trusted here do not
        /// give the permission to check what was true at the start.
        /// </summary>
        public String?                         Error              { get; }

        /// <summary>
        /// The price, where the session was started through an e-mobility
        /// provider, whose countersigned start is its start document.
        /// </summary>
        public SessionPriceCheck?              Price              { get; }

        /// <summary>
        /// The price per kWh the phone counted with.
        /// </summary>
        public Decimal                         SessionPricePerKWh { get; }

        /// <summary>
        /// The signed meter values of the charge detail record, once one came
        /// with signed data.
        /// </summary>
        public IReadOnlyList<MeterValueCheck>? MeterValues        { get; }

        /// <summary>
        /// Why the signed data of the record were not checked at all.
        /// </summary>
        public String?                         MeterValuesError   { get; }

        /// <summary>
        /// What the charge detail record says was charged, in kWh.
        /// </summary>
        public Decimal?                        CDREnergyKWh       { get; }

        /// <summary>
        /// What the charge detail record says it costs, with VAT.
        /// </summary>
        public Decimal?                        CDRCost            { get; }

        /// <summary>
        /// Whether every signed meter value of the record is the meter's.
        /// </summary>
        public Boolean                         MeterValuesVerified
            => MeterValuesError is null && MeterValues is { Count: > 0 } values && values.All(value => value.Verified);

        /// <summary>
        /// The reading the transaction began with, of those verified.
        /// </summary>
        public MeterReading?                   StartReading
            => Verified.FirstOrDefault(reading => reading.Transaction == "B") ?? Verified.FirstOrDefault();

        /// <summary>
        /// The reading the transaction ended with, of those verified.
        /// </summary>
        public MeterReading?                   EndReading
            => Verified.LastOrDefault(reading => reading.Transaction == "E") ?? Verified.LastOrDefault();

        /// <summary>
        /// What the meter signed was charged, in kWh: its end less its begin.
        /// </summary>
        public Decimal?                        SignedEnergyKWh
            => StartReading is MeterReading start && EndReading is MeterReading end && end != start &&
               start.EnergyKWh is Decimal from && end.EnergyKWh is Decimal to
                   ? to - from
                   : null;

        /// <summary>
        /// What the signed energy costs at the price checked, where the price
        /// is one of energy alone.
        /// </summary>
        public Decimal?                        ExpectedCost
            => SignedEnergyKWh is Decimal energy && Price?.Verification?.Selected is SelectedSessionPrice selected && EnergyOnly(selected.Elements) &&
               Price.PricePerKWh is Decimal price
                   ? Math.Round(energy * price, 2, MidpointRounding.AwayFromZero)
                   : null;

        private IEnumerable<MeterReading> Verified
            => (MeterValues ?? []).Where(value => value.Verified).
                                   SelectMany(value => value.Readings).
                                   OrderBy(reading => reading.MeasuredAt);

        #endregion

        #region Constructor(s)

        private SessionCheck(DateTimeOffset                   CheckedAt,
                             String?                          Error,
                             SessionPriceCheck?               Price,
                             Decimal                          SessionPricePerKWh,
                             IReadOnlyList<MeterValueCheck>?  MeterValues,
                             String?                          MeterValuesError,
                             Decimal?                         CDREnergyKWh,
                             Decimal?                         CDRCost)
        {
            this.CheckedAt           = CheckedAt;
            this.Error               = Error;
            this.Price               = Price;
            this.SessionPricePerKWh  = SessionPricePerKWh;
            this.MeterValues         = MeterValues;
            this.MeterValuesError    = MeterValuesError;
            this.CDREnergyKWh        = CDREnergyKWh;
            this.CDRCost             = CDRCost;
        }

        #endregion


        #region (static) Of(Configuration, Session, Now)

        /// <summary>
        /// Check the given session now, against the trust anchor and the
        /// identity keys trusted in the given configuration - or null, where it
        /// keeps no evidence: it was started at no station of a roaming network.
        /// </summary>
        public static SessionCheck? Of(RoamingNetworkConfiguration  Configuration,
                                       ChargingSession              Session,
                                       DateTimeOffset               Now)
        {

            if (Session.Evidence is not SessionEvidence evidence)
                return null;

            var cdr        = Session.Report?.CDR;
            var cdrEnergy  = Number(cdr?["total_energy"]);
            var cdrCost    = GrossCostOf(cdr?["total_cost"]);

            SessionCheck Unchecked(String Error)
                => new (Now, Error, null, Session.PricePerKWh, null, null, cdrEnergy, cdrCost);

            if (!RoamingNetworkProof.TryParse(evidence.Proofs[0], out var proof, out var proofError))
                return Unchecked($"The proof kept of {evidence.StationId} is none: {proofError}");

            // Of the decisions of now: a key no longer trusted checks nothing,
            // whatever it was trusted for at the start.
            if (Configuration.IsSet && !Configuration.QuorumTrusted)
                return Unchecked($"Nothing of it is checked: the identity keys trusted here are of fewer holders than the trust anchor of '{Configuration.TrustAnchor!.Id}' requires. Configuration → Map trusts them.");

            if (Configuration.HistoricalContext(proof, Now, out var authorityError) is not HistoricalTrustContext authority)
                return Unchecked($"Nothing of it is checked: {authorityError}");

            var price              = Session.Start is not null
                                         ? PriceOf(authority, evidence, Session)
                                         : null;

            var (values, problem)  = MeterValuesOf(authority, evidence, Session);

            return new SessionCheck(Now, null, price, Session.PricePerKWh, values, problem, cdrEnergy, cdrCost);

        }

        #endregion

        #region (private static) PriceOf(Authority, Evidence, Session)

        /// <summary>
        /// The price of the session as the price statements kept with it say,
        /// in force when it started - its start document the start as the
        /// provider countersigned it.
        /// </summary>
        private static SessionPriceCheck PriceOf(IRoamingNetworkTrustAuthority  Authority,
                                                 SessionEvidence                Evidence,
                                                 ChargingSession                Session)
        {

            if (!Evidence.PriceEvidence.TryVerify(Authority,
                                                  Evidence.EVSEId,
                                                  Evidence.MeterSerialNumber ?? "",
                                                  Session.StartedAt,
                                                  Session.Start!.RequestDigest,
                                                  out var verification,
                                                  out var error))
                return new SessionPriceCheck(null, error, null);

            return new SessionPriceCheck(verification,
                                         null,
                                         verification.Selected is SelectedSessionPrice selected
                                             ? StationCheck.EnergyPrice(new JArray(selected.Elements))
                                             : null);

        }

        #endregion

        #region (private static) MeterValuesOf(Authority, Evidence, Session)

        /// <summary>
        /// The signed meter values of the session's charge detail record, each
        /// checked - none, where no record with signed data came yet.
        /// </summary>
        private static (IReadOnlyList<MeterValueCheck>? Values, String? Error) MeterValuesOf(IRoamingNetworkTrustAuthority  Authority,
                                                                                            SessionEvidence                Evidence,
                                                                                            ChargingSession                Session)
        {

            if (Session.Report?.CDR is not JObject cdr)
                return (null, null);

            if (cdr["signed_data"] is not JObject signedData)
                return (null, "The charge detail record carries no signed meter values.");

            var method = signedData.Value<String>("encoding_method");

            if (!String.Equals(method, "OCMF", StringComparison.OrdinalIgnoreCase))
                return ([], $"Its signed meter values are encoded as '{method}', and this phone reads OCMF alone.");

            var values = (signedData["signed_values"] as JArray ?? []).OfType<JObject>().ToArray();

            if (values.Length == 0)
                return ([], "Its signed data holds no signed value.");

            var meterEvidence = Evidence.MeterEvidence;

            return ([ .. values.Select(value => Check(Authority, meterEvidence, Evidence.EVSEId, value, Session)) ], null);

        }

        #endregion

        #region (private static) Check(Authority, Evidence, EVSEId, Value, Session)

        /// <summary>
        /// One signed meter value: its OCMF document read by ChargyCore, the key
        /// of its meter at the times of its readings taken of the proofs kept,
        /// and its signature checked by ChargyCore with that key.
        /// </summary>
        private static MeterValueCheck Check(IRoamingNetworkTrustAuthority  Authority,
                                             HistoricalMeterEvidence        Evidence,
                                             String                         EVSEId,
                                             JObject                        Value,
                                             ChargingSession                Session)
        {

            var nature = Value.Value<String>("nature");

            if (OCMFText(Value) is not String text)
                return new MeterValueCheck(nature, false, "It is no OCMF document with a signature, neither as its signed data nor as its plain data.", null, null, null, null, null, false, []);

            OCMFDocument document;

            try
            {

                var scanned = new OCMFDocumentScanner().Scan([ text ]);

                if (scanned.Documents.Count != 1)
                    return new MeterValueCheck(nature, false, scanned.ErrorMessage ?? $"It holds {scanned.Documents.Count} OCMF documents, not one.", null, null, null, null, null, false, []);

                document = scanned.Documents[0];

            }
            catch (Exception e)
            {
                return new MeterValueCheck(nature, false, $"Its OCMF document could not be read: {e.Message}", null, null, null, null, null, false, []);
            }

            var payload         = document.Payload;
            var serial          = payload["MS"]?.Type == JTokenType.String ? payload["MS"]!.Value<String>() : null;
            var identification  = payload["ID"]?.Type == JTokenType.String ? payload["ID"]!.Value<String>() : null;
            var ofThisStart     = identification is not null &&
                                  (identification == Session.Start?.Token || identification == Session.Ticket?.Id);

            MeterValueCheck Refused(String Error, IReadOnlyList<MeterReading> Readings)
                => new (nature, false, Error, null, serial, null, null, identification, ofThisStart, Readings);

            if (!TryReadingsOf(payload["RD"], out var readings, out var readingsError))
                return Refused(readingsError, []);

            if (readings.Count == 0)
                return Refused("It holds no reading.", []);

            if (serial is null)
                return Refused("It names no meter: its \"MS\" is missing.", readings);

            if (!Evidence.TryVerify(Authority, EVSEId, serial, readings.Select(reading => reading.MeasuredAt), MeasurementKeyFormat.SPKIDER, out var verification, out var bindingError))
                return Refused($"No key of the meter {serial} of {EVSEId} is attested for when it was measured: {bindingError}", readings);

            var keys = verification.Bindings.DistinctBy(binding => binding.Key.CanonicalId).ToArray();

            if (keys.Length != 1)
                return Refused($"Its readings are of times its meter had {keys.Length} different keys.", readings);

            var binding = keys[0];

            if (binding.Key.Key.X is not Byte[] x || binding.Key.Key.Y is not Byte[] y)
                return Refused($"The key {binding.Key.Id} of the meter {serial} is no point of P-256.", readings);

            ChargyResult result;

            try
            {
                result = new OCMFSignatureValidator(chargyDictionary.Value).
                             Validate(document, P256SPKIPrefix + "04" + Convert.ToHexStringLower(x) + Convert.ToHexStringLower(y), "hex");
            }
            catch (Exception e)
            {
                return Refused($"ChargyCore could not check its signature: {e.Message}", readings);
            }

            var verified = result == ChargyResult.ValidSignature;

            return new MeterValueCheck(nature,
                                       verified,
                                       verified ? null : $"It is not signed by the key {binding.Key.Id} of the meter {serial}: {result}.",
                                       result.ToString(),
                                       serial,
                                       binding.MeterId,
                                       binding.Key.Id,
                                       identification,
                                       ofThisStart,
                                       readings);

        }

        #endregion

        #region (private static) OCMFText(Value) / TryReadingsOf(JSON, out Readings, out Error) / OCMFTime(Text)

        /// <summary>
        /// The OCMF document of a signed value - "OCMF|{payload}|{signature}" -
        /// as its signed data or its plain data say it, as text or as Base64.
        /// </summary>
        private static String? OCMFText(JObject Value)
        {

            static Boolean IsOCMF(String Text)
                => Text.StartsWith("OCMF|", StringComparison.Ordinal) && Text.Count(character => character == '|') >= 2;

            foreach (var name in new[] { "signed_data", "plain_data" })
            {

                if (Value[name]?.Type != JTokenType.String || Value.Value<String>(name)!.Trim() is not { Length: > 0 } text)
                    continue;

                if (IsOCMF(text))
                    return text;

                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(text)).Trim();

                    if (IsOCMF(decoded))
                        return decoded;
                }
                catch (FormatException)
                {
                    // Neither OCMF nor Base64 of it.
                }

            }

            return null;

        }

        /// <summary>
        /// The readings of an OCMF payload: each with the time the meter signed
        /// and its energy register in kWh - "Wh" taken as a thousandth.
        /// </summary>
        private static Boolean TryReadingsOf(JToken?                                              JSON,
                                             out IReadOnlyList<MeterReading>                      Readings,
                                             [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out String?  Error)
        {

            var readings = new List<MeterReading>();

            Readings  = readings;
            Error     = null;

            foreach (var reading in (JSON as JArray ?? []).OfType<JObject>())
            {

                if (OCMFTime(reading["TM"]?.Type == JTokenType.String ? reading["TM"]!.Value<String>() : null) is not DateTimeOffset measuredAt)
                {
                    Error = $"A reading names no time it was measured at as OCMF writes one: '{reading["TM"]}'.";
                    return false;
                }

                var value  = Number(reading["RV"]);
                var unit   = reading["RU"]?.Value<String>();

                readings.Add(new MeterReading(measuredAt,
                                              unit switch {
                                                  "kWh"  => value,
                                                  "Wh"   => value / 1000m,
                                                  _      => null
                                              },
                                              reading["TX"]?.Value<String>(),
                                              reading["ST"]?.Value<String>()));

            }

            return true;

        }

        /// <summary>
        /// When a reading of an OCMF document was taken, as it writes it:
        /// "2026-10-20T10:00:00,000+0000 S" - its offset without a colon, which
        /// no format string of .NET reads, and how its clock was synchronized.
        /// </summary>
        private static DateTimeOffset? OCMFTime(String? Text)
        {

            if (Text is null || Text.Length < 28)
                return null;

            if (!DateTime.TryParseExact(Text[..23], "yyyy-MM-dd'T'HH:mm:ss','fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) ||
                Text[23] is not ('+' or '-')                                                                                                     ||
                !Int32.TryParse(Text[24..26], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)                                    ||
                !Int32.TryParse(Text[26..28], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
                return null;

            return new DateTimeOffset(local, (Text[23] == '-' ? -1 : 1) * new TimeSpan(hours, minutes, 0));

        }

        #endregion

        #region (private static) EnergyOnly(Elements) / Number(JSON) / GrossCostOf(JSON)

        /// <summary>
        /// Whether the given tariff elements price energy and nothing else: one
        /// element, without restrictions, of energy components alone.
        /// </summary>
        private static Boolean EnergyOnly(IReadOnlyList<JObject> Elements)

            => Elements.Count == 1                                                                   &&
               Elements[0]["restrictions"] is null or JObject { HasValues: false }                   &&
               Elements[0]["priceComponents"] is JArray { Count: > 0 } components                    &&
               components.All(component => component is JObject json && json["type"]?.Value<String>() == "ENERGY");

        private static Decimal? Number(JToken? JSON)

            => JSON is JValue { Type: JTokenType.Integer or JTokenType.Float } number
                   ? number.Value<Decimal>()
                   : null;

        /// <summary>
        /// What an OCPI price says with VAT - or, as OCPI 2.1.1 writes a price,
        /// a number - and null where it says no more than without.
        /// </summary>
        private static Decimal? GrossCostOf(JToken? JSON)

            => JSON is JObject price
                   ? Number(price["incl_vat"])
                   : Number(JSON);

        #endregion


        #region ToJSON()

        /// <summary>
        /// The check as the pages show it.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("checkedAt",  CheckedAt.ToString("o")),
                           new JProperty("error",      Error)
                       );

            if (Price is SessionPriceCheck price)
            {

                var verification  = price.Verification;
                var selected      = verification?.Selected;

                json.Add(new JProperty("price", new JObject(
                             new JProperty("error",                   price.Error),
                             new JProperty("signatures",              verification?.Signatures),
                             new JProperty("applicability",           verification?.Applicability),
                             new JProperty("agreement",               verification?.Agreement),
                             new JProperty("publication",             verification?.Publication),
                             new JProperty("publicationBeforeStart",  verification?.PublicationBeforeStart),
                             new JProperty("chainGaps",               verification?.ChainGaps),
                             new JProperty("customerConsent",         verification?.CustomerConsent),
                             new JProperty("diagnostics",             new JArray(verification?.Diagnostics.ToArray() ?? [])),
                             new JProperty("selected",                selected is null ? null : new JObject(
                                 new JProperty("entityType",  selected.Statement.EntityType),
                                 new JProperty("entityId",    selected.Statement.EntityId),
                                 new JProperty("signer",      selected.Statement.Signer.ToString()),
                                 new JProperty("sequence",    selected.Statement.Sequence),
                                 new JProperty("signedAt",    selected.Statement.SignedAt.ToString("o")),
                                 new JProperty("digest",      selected.Digest),
                                 new JProperty("tariffId",    selected.Tariff["@id"]?.Value<String>()),
                                 new JProperty("currency",    selected.Currency),
                                 new JProperty("pricePerKWh", price.PricePerKWh)
                             )),
                             new JProperty("matchesSession",          price.PricePerKWh is Decimal perKWh ? perKWh == SessionPricePerKWh : (Boolean?) null)
                         )));

            }

            if (MeterValues is not null || MeterValuesError is not null)
                json.Add(new JProperty("meterValues", new JObject(
                             new JProperty("verified",      MeterValuesVerified),
                             new JProperty("error",         MeterValuesError),
                             new JProperty("values",        new JArray((MeterValues ?? []).Select(value => value.ToJSON()))),
                             new JProperty("startKWh",      StartReading?.EnergyKWh),
                             new JProperty("endKWh",        EndReading?.  EnergyKWh),
                             new JProperty("energyKWh",     SignedEnergyKWh),
                             new JProperty("cdrEnergyKWh",  CDREnergyKWh),
                             new JProperty("energyMatches", SignedEnergyKWh is Decimal signed && CDREnergyKWh is Decimal reported
                                                                ? Math.Round(signed, 3) == Math.Round(reported, 3)
                                                                : (Boolean?) null),
                             new JProperty("expectedCost",  ExpectedCost),
                             new JProperty("cdrCost",       CDRCost),
                             new JProperty("costMatches",   ExpectedCost is Decimal expected && CDRCost is Decimal cost
                                                                ? expected == Math.Round(cost, 2, MidpointRounding.AwayFromZero)
                                                                : (Boolean?) null),
                             new JProperty("timeBasis",     "signed-measurement-timestamps")
                         )));

            return json;

        }

        #endregion

    }

}
