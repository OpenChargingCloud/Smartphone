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

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// The limit a session was started with at the web payment page of the
    /// operator of its station, as the driver chose it there - and as OCPP
    /// 2.1 says it to the station, which keeps to it: the "transactionLimit"
    /// of its RequestStartTransaction, by its names and in its units. At most
    /// so much energy, so much cost, so long, or up to so full a battery - at
    /// least one of them.
    /// </summary>
    /// <param name="MaxEnergy">At most so much energy, in Wh.</param>
    /// <param name="MaxCost">At most so much cost, in the currency of the tariff.</param>
    /// <param name="MaxTime">At most so long, in seconds.</param>
    /// <param name="MaxSoC">Up to so full a battery, in percent.</param>
    public sealed record TransactionLimit(Decimal?  MaxEnergy  = null,
                                          Decimal?  MaxCost    = null,
                                          Int32?    MaxTime    = null,
                                          Int32?    MaxSoC     = null)
    {

        #region Data

        /// <summary>
        /// What a limit says, for a refusal.
        /// </summary>
        public const String Parts = "\"maxEnergy\" in Wh, \"maxCost\" in the currency of the tariff, \"maxTime\" in seconds, " +
                                    "\"maxSoC\" in percent - one of them at least";

        /// <summary>
        /// The names it says, as OCPP 2.1 names them - and "customData", which
        /// OCPP allows everywhere and which says nothing here.
        /// </summary>
        private static readonly HashSet<String> names = [ "maxEnergy", "maxCost", "maxTime", "maxSoC", "customData" ];

        #endregion


        #region ToJSON() / TryParse(JSON, out Limit, out Error)

        /// <summary>
        /// The limit as OCPP 2.1 says it.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (MaxEnergy is Decimal energy)  json.Add(new JProperty("maxEnergy",  energy));
            if (MaxCost   is Decimal cost)    json.Add(new JProperty("maxCost",    cost));
            if (MaxTime   is Int32   time)    json.Add(new JProperty("maxTime",    time));
            if (MaxSoC    is Int32   soc)     json.Add(new JProperty("maxSoC",     soc));

            return json;

        }

        /// <summary>
        /// The limit handed over - none where none was, or null was - or why
        /// what was handed over is none.
        /// </summary>
        public static Boolean TryParse(JToken?                                 JSON,
                                       out TransactionLimit?                   Limit,
                                       [NotNullWhen(false)] out String?        Error)
        {

            Limit  = null;
            Error  = null;

            if (JSON is null || JSON.Type == JTokenType.Null)
                return true;

            if (JSON is not JObject json)
            {
                Error = $"'limit' is the transactionLimit of OCPP 2.1 the driver chose at the web payment page: {Parts}.";
                return false;
            }

            if (json.Properties().FirstOrDefault(property => !names.Contains(property.Name)) is JProperty unknown)
            {
                Error = $"'limit' says '{unknown.Name}', which the transactionLimit of OCPP 2.1 does not know: it says {Parts}.";
                return false;
            }

            Decimal? Positive(String Name)

                => json[Name] is not JToken token || token.Type == JTokenType.Null
                       ? null
                       : token.Type is JTokenType.Integer or JTokenType.Float && token.Value<Decimal>() is var value && value > 0
                             ? value
                             : throw new FormatException($"'limit.{Name}' is a number more than nothing.");

            Int32? Whole(String Name, Int32 Max)

                => json[Name] is not JToken token || token.Type == JTokenType.Null
                       ? null
                       : token.Type == JTokenType.Integer && token.Value<Int64>() is var value && value > 0 && value <= Max
                             ? (Int32) value
                             : throw new FormatException($"'limit.{Name}' is a whole number from 1 to {Max}.");

            try
            {

                var limit = new TransactionLimit(Positive("maxEnergy"),
                                                 Positive("maxCost"),
                                                 Whole   ("maxTime",  Int32.MaxValue),
                                                 Whole   ("maxSoC",   100));

                if (limit.MaxEnergy is null && limit.MaxCost is null && limit.MaxTime is null && limit.MaxSoC is null)
                {
                    Error = $"'limit' names no limit: it says {Parts}.";
                    return false;
                }

                Limit = limit;
                return true;

            }
            catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException)
            {
                Error = e is FormatException ? e.Message : $"A part of 'limit' is no number this phone counts with: it says {Parts}.";
                return false;
            }

        }

        #endregion

        #region InWords(Currency)

        /// <summary>
        /// The limit in words, in the currency of the tariff: "20 kWh, 15.00
        /// EUR, 1 h 30 min, 80 % state of charge".
        /// </summary>
        public String InWords(String Currency)

            => String.Join(", ", new[] {
                                     MaxEnergy is Decimal energy  ? $"{KWh(energy / 1000m)} kWh"               : null,
                                     MaxCost   is Decimal cost    ? Money(cost, Currency)                       : null,
                                     MaxTime   is Int32   time    ? Duration(time)                              : null,
                                     MaxSoC    is Int32   soc     ? $"{soc} % state of charge"                  : null
                                 }.OfType<String>());

        #endregion


        #region (internal static) KWh(Value) / Money(Value, Currency) / Duration(Seconds)

        /// <summary>
        /// So much energy, in kWh: "20", "20.004".
        /// </summary>
        internal static String KWh(Decimal Value)
            => Value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// So much money, in a currency: "15.00 EUR".
        /// </summary>
        internal static String Money(Decimal Value, String Currency)
            => $"{Value.ToString("0.00", CultureInfo.InvariantCulture)} {Currency}";

        /// <summary>
        /// So long: "45 s", "52 min", "1 h 30 min".
        /// </summary>
        internal static String Duration(Decimal Seconds)
        {

            var seconds = (Int64) Math.Round(Math.Max(0m, Seconds));

            return seconds < 60
                       ? $"{seconds} s"
                       : seconds < 3600
                             ? $"{seconds / 60} min"
                             : seconds % 3600 / 60 == 0
                                   ? $"{seconds / 3600} h"
                                   : $"{seconds / 3600} h {seconds % 3600 / 60} min";

        }

        #endregion

    }

}
