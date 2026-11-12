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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.Smartphone.Provider
{

    /// <summary>
    /// The limits of a charging ticket, as the driver chose them for one
    /// start: at most so much energy, in kWh, and at most so long, in minutes
    /// - one of them at least. They are part of the ticket the e-mobility
    /// provider signs, and meant to become the station's.
    /// </summary>
    /// <remarks>
    /// Of a few values only, the same for everybody: a ticket with unusual
    /// limits is a fingerprint of its own, and tells the charge point
    /// operator two sessions of one driver apart from everybody else's. No
    /// limit of the power: no driver chooses one, and the provider takes a
    /// ticket without.
    /// </remarks>
    /// <param name="KWh">At most so much energy, in kWh.</param>
    /// <param name="Minutes">At most so long, in minutes.</param>
    public sealed record TicketLimits(Decimal?  KWh      = null,
                                      UInt32?   Minutes  = null)
    {

        #region Data

        /// <summary>
        /// The limits of energy a driver chooses among, in kWh.
        /// </summary>
        public static readonly IReadOnlyList<Decimal>  EnergyChoices  = [ 10m, 20m, 40m, 60m ];

        /// <summary>
        /// The limits of time a driver chooses among, in minutes.
        /// </summary>
        public static readonly IReadOnlyList<UInt32>   TimeChoices    = [ 30, 60, 120, 240 ];

        /// <summary>
        /// By how much what was charged may be over its limit and still have
        /// kept it: the station counts from its own start of the transaction
        /// and with its own rounding - a hundredth.
        /// </summary>
        public const Decimal Tolerance = 0.01m;

        /// <summary>
        /// What a limit says, for a refusal.
        /// </summary>
        public static String Parts

            => $"\"kWh\" - {String.Join(", ", EnergyChoices.Select(choice => choice.ToString(CultureInfo.InvariantCulture)))} - " +
               $"and \"minutes\" - {String.Join(", ", TimeChoices)} -, one of them at least";

        #endregion


        #region ToJSON() / TryParse(JSON, out Limits, out Error)

        public JObject ToJSON()
        {

            var json = new JObject();

            if (KWh     is Decimal kWh)      json.Add(new JProperty("kWh",      kWh));
            if (Minutes is UInt32  minutes)  json.Add(new JProperty("minutes",  minutes));

            return json;

        }

        /// <summary>
        /// The limits asked for - none where none were, or null was - or why
        /// what was asked for are none: of the few values everybody chooses
        /// among alone.
        /// </summary>
        public static Boolean TryParse(JToken?                           JSON,
                                       out TicketLimits?                 Limits,
                                       [NotNullWhen(false)] out String?  Error)
        {

            Limits  = null;
            Error   = null;

            if (JSON is null || JSON.Type == JTokenType.Null)
                return true;

            if (JSON is not JObject json)
            {
                Error = $"'limit' is the limits of the charging ticket of this start: {Parts}.";
                return false;
            }

            if (json.Properties().FirstOrDefault(property => property.Name is not ("kWh" or "minutes")) is JProperty unknown)
            {
                Error = $"'limit' says '{unknown.Name}', which a charging ticket of this phone does not limit: it says {Parts}.";
                return false;
            }

            Decimal?  kWh      = null;
            UInt32?   minutes  = null;

            if (json["kWh"] is JToken energy && energy.Type != JTokenType.Null)
            {

                if (energy.Type is not (JTokenType.Integer or JTokenType.Float) || !EnergyChoices.Contains(energy.AsDecimal()))
                {
                    Error = $"'limit.kWh' is one of {String.Join(", ", EnergyChoices.Select(choice => choice.ToString(CultureInfo.InvariantCulture)))}: " +
                             "a ticket with a limit of its own tells its driver apart.";
                    return false;
                }

                kWh = energy.AsDecimal();

            }

            if (json["minutes"] is JToken time && time.Type != JTokenType.Null)
            {

                if (time.Type != JTokenType.Integer || time.Value<Int64>() is var value && !TimeChoices.Any(choice => choice == value))
                {
                    Error = $"'limit.minutes' is one of {String.Join(", ", TimeChoices)}: a ticket with a limit of its own tells its driver apart.";
                    return false;
                }

                minutes = (UInt32) time.Value<Int64>();

            }

            if (kWh is null && minutes is null)
            {
                Error = $"'limit' names no limit: it says {Parts}.";
                return false;
            }

            Limits = new TicketLimits(kWh, minutes);
            return true;

        }

        /// <summary>
        /// The limits a file kept, whatever their values: null where it kept
        /// none.
        /// </summary>
        public static TicketLimits? TryParseStorage(JToken? JSON)
        {

            if (JSON is not JObject json)
                return null;

            var kWh      = json["kWh"]     is JToken { Type: JTokenType.Integer or JTokenType.Float } energy ? energy.AsDecimal() : (Decimal?) null;
            var minutes  = json["minutes"] is JToken { Type: JTokenType.Integer } time && time.Value<Int64>() is > 0 and <= UInt32.MaxValue
                               ? (UInt32) time.Value<Int64>()
                               : (UInt32?) null;

            return kWh is null && minutes is null
                       ? null
                       : new TicketLimits(kWh, minutes);

        }

        #endregion

        #region ToCBOR()

        /// <summary>
        /// The limits as a ticket says them: { "kWh", "minutes" }, each where
        /// it is set, a whole number as an integer.
        /// </summary>
        public CBORValue ToCBOR()
        {

            var limits = new List<KeyValuePair<CBORValue, CBORValue>>();

            if (KWh is Decimal kWh)
                limits.Add(new (CBORValue.FromText("kWh"),     kWh == Decimal.Truncate(kWh)
                                                                   ? CBORValue.FromInt64((Int64) kWh)
                                                                   : CBORValue.FromDouble((Double) kWh)));

            if (Minutes is UInt32 minutes)
                limits.Add(new (CBORValue.FromText("minutes"), CBORValue.FromInt64(minutes)));

            return CBORValue.FromMap(limits);

        }

        #endregion

        #region InWords

        /// <summary>
        /// The limits in words: "20 kWh, 1 h".
        /// </summary>
        public String InWords

            => String.Join(", ", new[] {
                                     KWh     is Decimal kWh      ? $"{kWh.ToString("0.###", CultureInfo.InvariantCulture)} kWh"  : null,
                                     Minutes is UInt32  minutes  ? Charging.TransactionLimit.Duration(minutes * 60m)            : null
                                 }.OfType<String>());

        #endregion

        #region Compared(EnergyKWh, EnergySigned, Seconds, TimeSigned)

        /// <summary>
        /// Each limit against what was charged: its value, whether the meter
        /// signed it, and whether the limit was kept - not over it by more
        /// than a hundredth; null where nothing says what was charged.
        /// </summary>
        /// <param name="EnergyKWh">What was charged, in kWh.</param>
        /// <param name="EnergySigned">Whether that is what the meter signed.</param>
        /// <param name="Seconds">For how long, in seconds.</param>
        /// <param name="TimeSigned">Whether that is what the meter signed.</param>
        public IReadOnlyList<TicketLimitValue> Compared(Decimal?  EnergyKWh,
                                                        Boolean   EnergySigned,
                                                        Decimal?  Seconds,
                                                        Boolean   TimeSigned)
        {

            static Boolean? Kept(Decimal? Value, Decimal Limit)
                => Value is Decimal value ? value <= Limit * (1m + Tolerance) : null;

            var compared = new List<TicketLimitValue>();

            if (KWh is Decimal kWh)
                compared.Add(new TicketLimitValue("energy", kWh, "kWh", EnergyKWh, EnergyKWh is not null && EnergySigned, Kept(EnergyKWh, kWh)));

            if (Minutes is UInt32 minutes)
                compared.Add(new TicketLimitValue("time", minutes * 60m, "s", Seconds, Seconds is not null && TimeSigned, Kept(Seconds, minutes * 60m)));

            return compared;

        }

        #endregion

    }


    /// <summary>
    /// One limit of a charging ticket against what was charged.
    /// </summary>
    /// <param name="Kind">"energy" or "time".</param>
    /// <param name="Limit">The limit, in its unit.</param>
    /// <param name="Unit">"kWh" or "s".</param>
    /// <param name="Value">What was charged, in its unit, or null where nothing says.</param>
    /// <param name="Signed">Whether the value is what the meter signed - else it is the word of the provider, or of this phone.</param>
    /// <param name="Kept">Whether the limit was kept; null where nothing says what was charged.</param>
    public sealed record TicketLimitValue(String    Kind,
                                          Decimal   Limit,
                                          String    Unit,
                                          Decimal?  Value,
                                          Boolean   Signed,
                                          Boolean?  Kept)
    {

        public JObject ToJSON()

            => new (new JProperty("kind",    Kind),
                    new JProperty("limit",   Limit),
                    new JProperty("unit",    Unit),
                    new JProperty("value",   Value),
                    new JProperty("signed",  Signed),
                    new JProperty("kept",    Kept));

    }

}
