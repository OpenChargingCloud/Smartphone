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
    /// transaction it is - "B" its begin, "E" its end - its status, "G" where
    /// the meter says it is good, and the errors the meter flags of it ("EF":
    /// "E" of the energy, "t" of the time), where it flags any.
    /// </summary>
    public sealed record MeterReading(DateTimeOffset  MeasuredAt,
                                      Decimal?        EnergyKWh,
                                      String?         Transaction,
                                      String?         Status,
                                      String?         ErrorFlags  = null)
    {

        public JObject ToJSON()
        {

            var json = new JObject(new JProperty("measuredAt",   MeasuredAt.ToString("o")),
                                   new JProperty("energyKWh",    EnergyKWh),
                                   new JProperty("transaction",  Transaction),
                                   new JProperty("status",       Status));

            if (ErrorFlags is { Length: > 0 })
                json.Add(new JProperty("errorFlags", ErrorFlags));

            return json;

        }

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

            if (AuthorityOf(Configuration, evidence, Now, out var authorityError) is not IRoamingNetworkTrustAuthority authority)
                return new SessionCheck(Now, authorityError, null, Session.PricePerKWh, null, null, cdrEnergy, cdrCost);

            var price              = Session.Start is not null
                                         ? PriceOf(authority, evidence, Session)
                                         : null;

            var (values, problem)  = MeterValuesOf(authority, evidence, Session);

            return new SessionCheck(Now, null, price, Session.PricePerKWh, values, problem, cdrEnergy, cdrCost);

        }

        #endregion

        #region (static) ValuesOf(Configuration, Session, Now)

        /// <summary>
        /// The signed documents of a session the web payment page of the
        /// operator of its station started, as its live link brought them,
        /// checked now - those not checked or not decided yet, in the order
        /// they came: its start value, the reading the meter signed as the
        /// begin of the transaction ("B"), and every value after it but the
        /// end value (<see cref="LiveLinkFollow.ToCheck"/>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A value holds where it is signed by the key the proofs kept give
        /// for the meter the proof of the station names for the EVSE, at the
        /// time it was measured, as ChargyCore checks the signature; where the
        /// meter says each of its readings is good - its status "G", no error
        /// flagged - and of an energy register; and where it fits the values
        /// that held before it: not measured before the start value, and the
        /// meter not counting backwards - no reading measured later with less
        /// energy, none measured earlier with more, none at the same time
        /// with another.
        /// </para>
        /// <para>
        /// It does not hold - a wrong meter value - where it is of another
        /// meter, not signed by that key, not good as the meter says, or does
        /// not fit. It is not checked where this phone can not decide: the
        /// identity keys trusted here do not suffice, or no proof kept attests
        /// a key of the meter for the time it was measured - one that names a
        /// time to come as well.
        /// </para>
        /// <para>
        /// That the values are of this very session is not proven: its live
        /// link names the charge point, and nothing binds the payment at the
        /// page to what the meter signs.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<(LiveLinkDocument Document, LiveLinkValueCheck Check)> ValuesOf(RoamingNetworkConfiguration  Configuration,
                                                                                                    ChargingSession              Session,
                                                                                                    DateTimeOffset               Now)
        {

            if (Session.LiveLink is not LiveLinkFollow follow || follow.ToCheck is not { Count: > 0 } toCheck)
                return [];

            if (Session.Evidence is not SessionEvidence evidence)
                return [.. toCheck.Select(document => (document, new LiveLinkValueCheck(Now, null, "The session keeps no proof of its station to check it with.")))];

            if (AuthorityOf(Configuration, evidence, Now, out var authorityError) is not IRoamingNetworkTrustAuthority authority)
                return [.. toCheck.Select(document => (document, new LiveLinkValueCheck(Now, null, authorityError)))];

            var start   = follow.StartDocument;
            var held    = follow.Signed.Where(document => document.Check?.Holds == true).SelectMany(document => document.Readings).ToList();
            var begin   = start?.Document.Check?.Holds == true ? start.Value.Reading : null;
            var checks  = new List<(LiveLinkDocument, LiveLinkValueCheck)>();

            foreach (var document in toCheck)
            {

                var isStart  = ReferenceEquals(document, start?.Document);
                var check    = CheckValue(authority, evidence, document, isStart, isStart ? null : begin, held, Session, Now);

                checks.Add((document, check));

                // A wrong one stops the session: nothing after it is checked.
                if (check.Holds == false)
                    break;

                if (check.Holds == true)
                {

                    held.AddRange(document.Readings);

                    if (isStart)
                        begin = start!.Value.Reading;

                }

            }

            return checks;

        }

        #endregion

        #region (private static) CheckValue(Authority, Evidence, Document, IsStart, Begin, Held, Session, Now)

        /// <summary>
        /// One signed document of a live link, checked: its signature with the
        /// key of the meter, what the meter says of its readings, and how they
        /// fit the begin of the transaction and the readings that held before.
        /// </summary>
        private static LiveLinkValueCheck CheckValue(IRoamingNetworkTrustAuthority  Authority,
                                                     SessionEvidence                Evidence,
                                                     LiveLinkDocument               Document,
                                                     Boolean                        IsStart,
                                                     MeterReading?                  Begin,
                                                     IReadOnlyList<MeterReading>    Held,
                                                     ChargingSession                Session,
                                                     DateTimeOffset                 Now)
        {

            var value = CheckDocument(Authority, Evidence.MeterEvidence, Evidence.EVSEId, IsStart ? "Start" : "Intermediate", Document.Text, Session, out var noKey);

            LiveLinkValueCheck Checked(Boolean? Holds, String? Error)
                => new (Now, Holds, Error, value.MeterSerialNumber, value.MeterId, value.KeyId, value.Result);

            if (!value.Verified)
                return value.MeterSerialNumber is String named && Evidence.MeterSerialNumber is String expected && named != expected
                           ? Checked(false, $"It is of the meter {named}, and the proof of {Evidence.StationId} names the meter {expected} for {Evidence.EVSEId}.")
                           : Checked(noKey ? null : false, value.Error);

            // The meter's: what the meter says of it itself.
            foreach (var reading in Document.Readings)
            {

                var which = IsStart && reading.Transaction == "B" ? "its start value" : $"its value of {reading.MeasuredAt:o}";

                if (reading.Status is String status && status != "G")
                    return Checked(false, $"The meter says {which} is not good: its status is \"{status}\"{StatusInWords(status)}.");

                if (reading.ErrorFlags is { Length: > 0 } flags)
                    return Checked(false, $"The meter flags an error of {which}: \"{flags}\"{ErrorFlagsInWords(flags)}.");

                if (reading.EnergyKWh is null)
                    return Checked(false, $"{Capital(which)} is no energy register in kWh or Wh.");

            }

            // How it fits what held before it.
            foreach (var reading in Document.Readings)
            {

                if (Begin is MeterReading begin && reading.MeasuredAt < begin.MeasuredAt)
                    return Checked(false, $"It was measured at {reading.MeasuredAt:o}, before the start value of {begin.MeasuredAt:o}.");

                foreach (var before in Held)
                {

                    if (before.EnergyKWh is not Decimal was)
                        continue;

                    if ((before.MeasuredAt < reading.MeasuredAt && was > reading.EnergyKWh) ||
                        (before.MeasuredAt > reading.MeasuredAt && was < reading.EnergyKWh))
                        return Checked(false, $"The meter counts backwards: {Figure(reading.EnergyKWh!.Value)} kWh at {reading.MeasuredAt:o}, and {Figure(was)} kWh at {before.MeasuredAt:o}.");

                    if (before.MeasuredAt == reading.MeasuredAt && was != reading.EnergyKWh)
                        return Checked(false, $"It says {Figure(reading.EnergyKWh!.Value)} kWh at {reading.MeasuredAt:o}, where a value before says {Figure(was)} kWh at that time.");

                }

            }

            return Checked(true, null);

        }

        private static String Capital(String Text)
            => Text.Length > 0 ? Char.ToUpperInvariant(Text[0]) + Text[1..] : Text;

        private static String Figure(Decimal Value)
            => Value.ToString(CultureInfo.InvariantCulture);

        #endregion

        #region (private static) AuthorityOf(Configuration, Evidence, Now, out Error)

        /// <summary>
        /// What the evidence of a session is checked under now: the proof of
        /// its start, under the trust anchor and the identity keys trusted in
        /// the given configuration - or why nothing of it is checked.
        /// </summary>
        private static IRoamingNetworkTrustAuthority? AuthorityOf(RoamingNetworkConfiguration  Configuration,
                                                                  SessionEvidence              Evidence,
                                                                  DateTimeOffset               Now,
                                                                  out String?                  Error)
        {

            Error = null;

            if (!RoamingNetworkProof.TryParse(Evidence.Proofs[0], out var proof, out var proofError))
            {
                Error = $"The proof kept of {Evidence.StationId} is none: {proofError}";
                return null;
            }

            // Of the decisions of now: a key no longer trusted checks nothing,
            // whatever it was trusted for at the start.
            if (Configuration.IsSet && !Configuration.QuorumTrusted)
            {
                Error = $"Nothing of it is checked: the identity keys trusted here are of fewer holders than the trust anchor of '{Configuration.TrustAnchor!.Id}' requires. Configuration → Map trusts them.";
                return null;
            }

            if (Configuration.HistoricalContext(proof, Now, out var authorityError) is not HistoricalTrustContext authority)
            {
                Error = $"Nothing of it is checked: {authorityError}";
                return null;
            }

            return authority;

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
        /// One signed meter value of a charge detail record: its OCMF document,
        /// as its signed data or its plain data say it, checked.
        /// </summary>
        private static MeterValueCheck Check(IRoamingNetworkTrustAuthority  Authority,
                                             HistoricalMeterEvidence        Evidence,
                                             String                         EVSEId,
                                             JObject                        Value,
                                             ChargingSession                Session)
        {

            var nature = Value.Value<String>("nature");

            return OCMFText(Value) is String text
                       ? CheckDocument(Authority, Evidence, EVSEId, nature, text, Session, out _)
                       : new MeterValueCheck(nature, false, "It is no OCMF document with a signature, neither as its signed data nor as its plain data.", null, null, null, null, null, false, []);

        }

        #endregion

        #region (private static) CheckDocument(Authority, Evidence, EVSEId, Nature, Text, Session, out NoKey)

        /// <summary>
        /// One signed meter value: its OCMF document read by ChargyCore, the key
        /// of its meter at the times of its readings taken of the proofs kept,
        /// and its signature checked by ChargyCore with that key. Where it is
        /// not the meter's, whether that is because no key of its meter could
        /// be had to check it with (NoKey) - no proof kept attests one for its
        /// times, or more than one - rather than that it is none.
        /// </summary>
        private static MeterValueCheck CheckDocument(IRoamingNetworkTrustAuthority  Authority,
                                                     HistoricalMeterEvidence        Evidence,
                                                     String                         EVSEId,
                                                     String?                        Nature,
                                                     String                         Text,
                                                     ChargingSession                Session,
                                                     out Boolean                    NoKey)
        {

            NoKey = false;

            OCMFDocument document;

            try
            {

                var scanned = new OCMFDocumentScanner().Scan([ Text ]);

                if (scanned.Documents.Count != 1)
                    return new MeterValueCheck(Nature, false, scanned.ErrorMessage ?? $"It holds {scanned.Documents.Count} OCMF documents, not one.", null, null, null, null, null, false, []);

                document = scanned.Documents[0];

            }
            catch (Exception e)
            {
                return new MeterValueCheck(Nature, false, $"Its OCMF document could not be read: {e.Message}", null, null, null, null, null, false, []);
            }

            var payload         = document.Payload;
            var serial          = payload["MS"]?.Type == JTokenType.String ? payload["MS"]!.Value<String>() : null;
            var identification  = payload["ID"]?.Type == JTokenType.String ? payload["ID"]!.Value<String>() : null;
            var ofThisStart     = identification is not null &&
                                  (identification == Session.Start?.Token || identification == Session.Ticket?.Id);

            MeterValueCheck Refused(String Error, IReadOnlyList<MeterReading> Readings)
                => new (Nature, false, Error, null, serial, null, null, identification, ofThisStart, Readings);

            if (!TryReadingsOf(payload["RD"], out var readings, out var readingsError))
                return Refused(readingsError, []);

            if (readings.Count == 0)
                return Refused("It holds no reading.", []);

            if (serial is null)
                return Refused("It names no meter: its \"MS\" is missing.", readings);

            NoKey = true;

            if (!Evidence.TryVerify(Authority, EVSEId, serial, readings.Select(reading => reading.MeasuredAt), MeasurementKeyFormat.SPKIDER, out var verification, out var bindingError))
                return Refused($"No key of the meter {serial} of {EVSEId} is attested for when it was measured: {bindingError}", readings);

            var keys = verification.Bindings.DistinctBy(binding => binding.Key.CanonicalId).ToArray();

            if (keys.Length != 1)
                return Refused($"Its readings are of times its meter had {keys.Length} different keys.", readings);

            var binding = keys[0];

            if (binding.Key.Key.X is not Byte[] x || binding.Key.Key.Y is not Byte[] y)
                return Refused($"The key {binding.Key.Id} of the meter {serial} is no point of P-256.", readings);

            NoKey = false;

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

            return new MeterValueCheck(Nature,
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
        internal static Boolean TryReadingsOf(JToken?                                             JSON,
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
                                              reading["ST"]?.Value<String>(),
                                              reading["EF"]?.Type == JTokenType.String ? reading["EF"]!.Value<String>() : null));

            }

            return true;

        }

        /// <summary>
        /// What the status of an OCMF reading ("ST") means, as " (...)" - or
        /// nothing, where OCMF knows no such status.
        /// </summary>
        private static String StatusInWords(String Status)

            => Status switch {
                   "N"  => " (not present)",
                   "T"  => " (timeout)",
                   "D"  => " (disconnected)",
                   "R"  => " (not found)",
                   "M"  => " (manipulated)",
                   "X"  => " (exchanged)",
                   "I"  => " (incompatible)",
                   "O"  => " (out of range)",
                   "S"  => " (substitute)",
                   "E"  => " (system error)",
                   "F"  => " (read error)",
                   _    => ""
               };

        /// <summary>
        /// What the error flags of an OCMF reading ("EF") say, as " (...)":
        /// "E" of its energy, "t" of its time.
        /// </summary>
        private static String ErrorFlagsInWords(String Flags)
        {

            var of = new List<String>();

            if (Flags.Contains('E'))  of.Add("of its energy");
            if (Flags.Contains('t'))  of.Add("of its time");

            return of.Count > 0 ? $" ({String.Join(" and ", of)})" : "";

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
