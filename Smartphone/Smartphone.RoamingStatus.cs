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

using cloud.charging.open.Smartphone.Map;
using cloud.charging.open.Smartphone.Roaming;

#endregion

namespace cloud.charging.open.Smartphone
{

    public partial class Smartphone
    {

        #region Data

        private readonly Object                 roamingStatusLock  = new();
        private          RoamingNetworkStatus?  roamingStatus;
        private          Boolean                followsStatus;

        #endregion

        #region Properties

        /// <summary>
        /// The status of the charge points of the roaming network whose trust
        /// anchor is set, followed over its WebSocket while the phone runs -
        /// or null, where there is none.
        /// </summary>
        public RoamingNetworkStatus? RoamingStatus
        {
            get
            {
                lock (roamingStatusLock)
                    return roamingStatus;
            }
        }

        #endregion


        #region RoamingStatusOf(EVSE)

        /// <summary>
        /// The status of an EVSE of the roaming network as it says it now - or
        /// unknown, where it is not reached, or says nothing of it.
        /// </summary>
        public String RoamingStatusOf(ChargingEVSE EVSE)

            => RoamingStatus?.StatusOf(EVSE.EVSEId) ?? EVSEStatus.Unknown;

        #endregion

        #region (private) FollowRoamingStatus()

        /// <summary>
        /// Follow the status of the roaming network whose trust anchor is set
        /// now, and of no other: of the one before no longer, where the anchor
        /// changed - and of none while the phone does not run.
        /// </summary>
        private void FollowRoamingStatus()
        {

            RoamingNetworkStatus? before;

            lock (roamingStatusLock)
            {

                var anchor = RoamingNetwork.TrustAnchor;

                if (followsStatus && anchor is not null && roamingStatus is not null &&
                    roamingStatus.TrustAnchor.ToJSON().ToString() == anchor.ToJSON().ToString())
                    return;

                before         = roamingStatus;
                roamingStatus  = followsStatus && anchor is not null
                                     ? new RoamingNetworkStatus(anchor, DNSClient, TimeProvider, Log)
                                     : null;

                roamingStatus?.Start();

            }

            // Not waited for: what the one before still says is not asked any
            // more, and its connection closes on its own time.
            if (before is not null)
                _ = before.DisposeAsync().AsTask();

        }

        #endregion

        #region (private) StartFollowingRoamingStatus() / StopFollowingRoamingStatusAsync()

        private void StartFollowingRoamingStatus()
        {

            lock (roamingStatusLock)
                followsStatus = true;

            FollowRoamingStatus();

        }

        private async Task StopFollowingRoamingStatusAsync()
        {

            RoamingNetworkStatus? before;

            lock (roamingStatusLock)
            {
                followsStatus  = false;
                before         = roamingStatus;
                roamingStatus  = null;
            }

            if (before is not null)
                await before.DisposeAsync();

        }

        #endregion

    }

}
