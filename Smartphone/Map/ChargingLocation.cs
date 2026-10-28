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

namespace cloud.charging.open.Smartphone.Map
{

    /// <summary>
    /// The status of an EVSE, in OCPI's words - so that the stations of a URL
    /// that speaks OCPI drop in where the demo ones are now.
    /// </summary>
    public static class EVSEStatus
    {

        public const String Available    = "AVAILABLE";
        public const String Blocked      = "BLOCKED";
        public const String Charging     = "CHARGING";
        public const String Inoperative  = "INOPERATIVE";
        public const String OutOfOrder   = "OUTOFORDER";
        public const String Planned      = "PLANNED";
        public const String Removed      = "REMOVED";
        public const String Reserved     = "RESERVED";
        public const String Unknown      = "UNKNOWN";

        /// <summary>
        /// Every status, in the order a legend shows them.
        /// </summary>
        public static readonly IReadOnlyList<String> All = [
            Available, Charging, Reserved, Blocked, Inoperative, OutOfOrder, Planned, Removed, Unknown
        ];

        /// <summary>
        /// A status as a sentence says it: "out of order", not "outoforder".
        /// </summary>
        public static String InWords(String Status)

            => Status switch {
                   OutOfOrder  => "out of order",
                   _           => Status.ToLowerInvariant()
               };

    }


    /// <summary>
    /// One charge point of a location: an EVSE with its one connector.
    /// </summary>
    /// <param name="Uid">Its identification within the location.</param>
    /// <param name="EVSEId">Its eMI3 EVSE ID, as printed on it.</param>
    /// <param name="Standard">The connector, in OCPI's words: "IEC_62196_T2", "IEC_62196_T2_COMBO", "CHADEMO".</param>
    /// <param name="Format">"SOCKET" or "CABLE".</param>
    /// <param name="PowerType">"AC_3_PHASE" or "DC".</param>
    /// <param name="MaxPowerKW">The most it gives, in kW.</param>
    /// <param name="StationId">Of a roaming network: the charging station it belongs to, whose proof says what is signed about it.</param>
    public sealed record ChargingEVSE(String   Uid,
                                      String   EVSEId,
                                      String   Standard,
                                      String   Format,
                                      String   PowerType,
                                      Decimal  MaxPowerKW,
                                      String?  StationId   = null)
    {

        /// <summary>
        /// Whether it charges with direct current.
        /// </summary>
        public Boolean IsDC
            => PowerType == "DC";

        /// <summary>
        /// The EVSE as an OCPI location carries it, with the status it has now
        /// - and, of a roaming network, the station it belongs to.
        /// </summary>
        public JObject ToJSON(String Status)
        {

            var json = new JObject(
                           new JProperty("uid",         Uid),
                           new JProperty("evse_id",     EVSEId),
                           new JProperty("status",      Status),
                           new JProperty("connectors",  new JArray(
                               new JObject(
                                   new JProperty("id",                  "1"),
                                   new JProperty("standard",            Standard),
                                   new JProperty("format",              Format),
                                   new JProperty("power_type",          PowerType),
                                   new JProperty("max_electric_power",  (Int64) (MaxPowerKW * 1000))
                               )
                           ))
                       );

            if (StationId is not null)
                json.Property("evse_id")!.AddAfterSelf(new JProperty("station_id", StationId));

            return json;

        }

    }


    /// <summary>
    /// What charging at a location costs - one price per kWh, which is all a
    /// demo station says and all the simulated session counts with - and, at a
    /// station of a roaming network, the tariff of its proof it is of.
    /// </summary>
    public sealed record ChargingTariff(Decimal  PricePerKWh,
                                        String   Currency,
                                        String?  TariffId  = null);


    /// <summary>
    /// One place to charge, as an OCPI location says it: where it is, who runs
    /// it, and its EVSEs.
    /// </summary>
    /// <remarks>
    /// A demo station says what charging there costs; a charging pool of a
    /// roaming network says it only in the proof of its stations, so it has
    /// no tariff until that proof was checked.
    /// </remarks>
    public sealed record ChargingLocation(String                       Id,
                                          String                       Name,
                                          String                       Address,
                                          String                       PostalCode,
                                          String                       City,
                                          String                       Country,
                                          Double                       Latitude,
                                          Double                       Longitude,
                                          String                       Operator,
                                          ChargingTariff?              Tariff,
                                          IReadOnlyList<ChargingEVSE>  EVSEs)
    {

        /// <summary>
        /// One of its EVSEs, by its uid.
        /// </summary>
        public ChargingEVSE? EVSE(String Uid)

            => EVSEs.FirstOrDefault(evse => evse.Uid == Uid);

        /// <summary>
        /// The location as OCPI carries one - coordinates as text, as OCPI has
        /// them - with the status each EVSE has now, and its price beside it.
        /// </summary>
        /// <param name="StatusOf">The status of one of its EVSEs now.</param>
        /// <param name="LastUpdated">When what is said was true.</param>
        public JObject ToJSON(Func<ChargingEVSE, String>  StatusOf,
                              DateTimeOffset              LastUpdated)

            => new (
                   new JProperty("id",            Id),
                   new JProperty("name",          Name),
                   new JProperty("address",       Address),
                   new JProperty("postal_code",   PostalCode),
                   new JProperty("city",          City),
                   new JProperty("country",       Country),
                   new JProperty("coordinates",   new JObject(
                       new JProperty("latitude",   Latitude. ToString("0.000000", CultureInfo.InvariantCulture)),
                       new JProperty("longitude",  Longitude.ToString("0.000000", CultureInfo.InvariantCulture))
                   )),
                   new JProperty("operator",      new JObject(new JProperty("name", Operator))),
                   new JProperty("evses",         new JArray(EVSEs.Select(evse => evse.ToJSON(StatusOf(evse))))),
                   new JProperty("tariff",        Tariff is not null
                                                      ? new JObject(
                                                            new JProperty("price_per_kwh",  Tariff.PricePerKWh),
                                                            new JProperty("currency",       Tariff.Currency)
                                                        )
                                                      : null),
                   new JProperty("last_updated",  LastUpdated.ToString("o"))
               );

    }

}
