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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.Smartphone
{

    /// <summary>
    /// What a smartphone adds to the resources every node has, and the roles
    /// of the people who carry it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two resources, because they are two different things to be trusted
    /// with. The map is looked at by anybody who holds the phone; where it
    /// takes its charging stations from is a setting, and changing it points
    /// the phone at somebody else's idea of where one may charge. Charging is
    /// money: starting a session is what a driver does, and what a viewer of
    /// the phone may not.
    /// </para>
    /// <para>
    /// The node brings the viewer, who may look at everything, and the
    /// administrators, who may do everything - "root", the driver whose phone
    /// this is, among them. The configuration file may add roles and say
    /// differently what one of them may do - see the node's "roles" section.
    /// </para>
    /// </remarks>
    public static class SmartphoneAccess
    {

        #region Resources

        /// <summary>
        /// The map: read, the charging stations and where they come from;
        /// edited, where they come from.
        /// </summary>
        public const String  Map       = "map";

        /// <summary>
        /// Charging: read, the sessions of this phone, running and past; run,
        /// one started or stopped.
        /// </summary>
        public const String  Charging  = "charging";

        /// <summary>
        /// Both.
        /// </summary>
        public static readonly IReadOnlyList<String>  Resources = [ Map, Charging ];

        #endregion

        #region Roles

        /// <summary>
        /// Somebody who charges with this phone: looks at the map and at the
        /// sessions, and starts and stops them - but does not repoint the map
        /// at another source, and changes nothing else about the phone.
        /// </summary>
        public static readonly Role  Driver  = new ("driver",
                                                    [ Permission.Read(Map),
                                                      Permission.Read(Charging),
                                                      Permission.Run (Charging) ],
                                                    "charges with this phone: looks at the map and the sessions, and starts and stops one");

        /// <summary>
        /// The one role a smartphone adds.
        /// </summary>
        public static readonly IReadOnlyList<Role>  Roles = [ Driver ];

        #endregion

    }

}
