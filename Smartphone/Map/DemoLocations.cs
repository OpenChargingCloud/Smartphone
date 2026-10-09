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

namespace cloud.charging.open.Smartphone.Map
{

    /// <summary>
    /// The charging stations the map shows until it asks a URL for real ones:
    /// a handful in and around Jena, made up, with operators made up too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of their EVSEs come and go between available and charging, as if
    /// other drivers used them: which one is busy changes every ten minutes,
    /// and is the same for everybody who asks within those ten minutes -
    /// worked out from the EVSE and the time rather than drawn at random, so
    /// that a test can say what it will be. A few stay as they are - out of
    /// order, inoperative, planned - so that the map has every colour to show.
    /// </para>
    /// <para>
    /// An EVSE this phone is charging at is charging, whatever the ten
    /// minutes say: that is the smartphone's to add, see
    /// <see cref="Smartphone.LocationsJSON"/>.
    /// </para>
    /// </remarks>
    public static class DemoLocations
    {

        #region Data

        /// <summary>
        /// How long one EVSE's coming and going stays as it is.
        /// </summary>
        public static readonly TimeSpan Slot = TimeSpan.FromMinutes(10);

        private const String T2     = "IEC_62196_T2";
        private const String CCS    = "IEC_62196_T2_COMBO";
        private const String AC     = "AC_3_PHASE";
        private const String DC     = "DC";

        private static readonly ChargingTariff ACTariff   = new (0.49m, "EUR");
        private static readonly ChargingTariff DCTariff   = new (0.69m, "EUR");
        private static readonly ChargingTariff HPCTariff  = new (0.79m, "EUR");

        /// <summary>
        /// The EVSEs that do not come and go, and what they stay.
        /// </summary>
        private static readonly IReadOnlyDictionary<String, String> Fixed = new Dictionary<String, String> {
            [ "DE*GDF*E1004*2" ]  = EVSEStatus.OutOfOrder,
            [ "DE*GDF*E1007*1" ]  = EVSEStatus.Inoperative,
            [ "DE*SST*E2003*1" ]  = EVSEStatus.Reserved,
            [ "DE*THP*E3002*1" ]  = EVSEStatus.Planned,
            [ "DE*THP*E3002*2" ]  = EVSEStatus.Planned
        };

        #endregion

        #region All

        /// <summary>
        /// Every demo location.
        /// </summary>
        public static readonly IReadOnlyList<ChargingLocation> All = [

            new ("DEMO-JENA-HOLZMARKT",    "Jena Holzmarkt",               "Holzmarkt 1",            "07743", "Jena",   "DEU", 50.928420, 11.587610, "Saaletal Strom (demo)",     ACTariff,  [
                 new ("1", "DE*SST*E2001*1", T2,  "SOCKET", AC,  22m),
                 new ("2", "DE*SST*E2001*2", T2,  "SOCKET", AC,  22m)
            ]),

            new ("DEMO-JENA-PARADIES",     "Jena Paradies Bahnhof",        "Am Bahnhof Paradies 2",  "07745", "Jena",   "DEU", 50.922110, 11.586830, "GraphDefined Charging (demo)", DCTariff, [
                 new ("1", "DE*GDF*E1001*1", CCS, "CABLE",  DC,  150m),
                 new ("2", "DE*GDF*E1001*2", CCS, "CABLE",  DC,  150m),
                 new ("3", "DE*GDF*E1001*3", T2,  "SOCKET", AC,  22m)
            ]),

            new ("DEMO-JENA-ABBEPLATZ",    "Ernst-Abbe-Platz",             "Ernst-Abbe-Platz 4",     "07743", "Jena",   "DEU", 50.929520, 11.583480, "Saaletal Strom (demo)",     ACTariff,  [
                 new ("1", "DE*SST*E2002*1", T2,  "SOCKET", AC,  11m),
                 new ("2", "DE*SST*E2002*2", T2,  "SOCKET", AC,  11m),
                 new ("3", "DE*SST*E2002*3", T2,  "SOCKET", AC,  11m),
                 new ("4", "DE*SST*E2002*4", T2,  "SOCKET", AC,  11m)
            ]),

            new ("DEMO-JENA-WEST",         "Jena West Bahnhof",            "Westbahnhofstraße 8",    "07745", "Jena",   "DEU", 50.924730, 11.574820, "Saaletal Strom (demo)",     ACTariff,  [
                 new ("1", "DE*SST*E2003*1", T2,  "SOCKET", AC,  22m),
                 new ("2", "DE*SST*E2003*2", T2,  "SOCKET", AC,  22m)
            ]),

            new ("DEMO-JENA-BEUTENBERG",   "Beutenberg Campus",            "Hans-Knöll-Straße 1",    "07745", "Jena",   "DEU", 50.912960, 11.569880, "GraphDefined Charging (demo)", DCTariff, [
                 new ("1", "DE*GDF*E1004*1", CCS, "CABLE",  DC,  50m),
                 new ("2", "DE*GDF*E1004*2", T2,  "SOCKET", AC,  22m)
            ]),

            new ("DEMO-JENA-LOBEDA",       "Lobeda Stadtteilzentrum",      "Karl-Marx-Allee 20",     "07747", "Jena",   "DEU", 50.894480, 11.606210, "Thüringen HPC (demo)",      HPCTariff, [
                 new ("1", "DE*THP*E3001*1", CCS, "CABLE",  DC,  300m),
                 new ("2", "DE*THP*E3001*2", CCS, "CABLE",  DC,  300m)
            ]),

            new ("DEMO-JENA-GOESCHWITZ",   "Rastplatz Jena-Göschwitz",     "Rudolstädter Straße 90", "07745", "Jena",   "DEU", 50.886920, 11.587140, "Thüringen HPC (demo)",      HPCTariff, [
                 new ("1", "DE*THP*E3003*1", CCS, "CABLE",  DC,  350m),
                 new ("2", "DE*THP*E3003*2", CCS, "CABLE",  DC,  350m),
                 new ("3", "DE*THP*E3003*3", CCS, "CABLE",  DC,  350m),
                 new ("4", "DE*THP*E3003*4", CCS, "CABLE",  DC,  350m)
            ]),

            new ("DEMO-JENA-NORD",         "Jena Nord Löbstedt",           "Löbstedter Straße 70",   "07749", "Jena",   "DEU", 50.947980, 11.598150, "Thüringen HPC (demo)",      HPCTariff, [
                 new ("1", "DE*THP*E3002*1", CCS, "CABLE",  DC,  300m),
                 new ("2", "DE*THP*E3002*2", CCS, "CABLE",  DC,  300m)
            ]),

            new ("DEMO-JENA-ZEISS",        "Zeiss-Planetarium",            "Am Planetarium 5",       "07743", "Jena",   "DEU", 50.932140, 11.584860, "GraphDefined Charging (demo)", ACTariff, [
                 new ("1", "DE*GDF*E1007*1", T2,  "SOCKET", AC,  22m),
                 new ("2", "DE*GDF*E1007*2", T2,  "SOCKET", AC,  22m)
            ]),

            new ("DEMO-WEIMAR-GOETHEPLATZ", "Weimar Goetheplatz",          "Goetheplatz 9",          "99423", "Weimar", "DEU", 50.980610, 11.326040, "Saaletal Strom (demo)",     ACTariff,  [
                 new ("1", "DE*SST*E2004*1", T2,  "SOCKET", AC,  22m),
                 new ("2", "DE*SST*E2004*2", T2,  "SOCKET", AC,  22m)
            ]),

            new ("DEMO-ERFURT-DOMPLATZ",   "Erfurt Domplatz",              "Domplatz 30",            "99084", "Erfurt", "DEU", 50.976890, 11.022510, "GraphDefined Charging (demo)", DCTariff, [
                 new ("1", "DE*GDF*E1008*1", CCS, "CABLE",  DC,  150m),
                 new ("2", "DE*GDF*E1008*2", T2,  "SOCKET", AC,  22m)
            ])

        ];

        #endregion


        #region Location(Id)

        /// <summary>
        /// One demo location, by its id.
        /// </summary>
        public static ChargingLocation? Location(String Id)

            => All.FirstOrDefault(location => location.Id == Id);

        #endregion

        #region StatusOf(EVSE, Now)

        /// <summary>
        /// What a demo EVSE says of itself at the given time, before anything
        /// this phone does is taken into account: what it stays, or whether
        /// somebody else is charging there during these ten minutes - one in
        /// three of them, at any moment.
        /// </summary>
        public static String StatusOf(ChargingEVSE    EVSE,
                                      DateTimeOffset  Now)
        {

            if (Fixed.TryGetValue(EVSE.EVSEId, out var status))
                return status;

            var slot = Now.ToUnixTimeSeconds() / (Int64) Slot.TotalSeconds;

            return StableHash($"{EVSE.EVSEId}@{slot}") % 3 == 0
                       ? EVSEStatus.Charging
                       : EVSEStatus.Available;

        }

        #endregion

        #region (private) StableHash(Text)

        /// <summary>
        /// FNV-1a over the text: the same number in every process, which
        /// String.GetHashCode() is not.
        /// </summary>
        private static UInt32 StableHash(String Text)
        {

            var hash = 2166136261u;

            foreach (var character in Text)
                hash = (hash ^ character) * 16777619u;

            return hash;

        }

        #endregion

    }

}
