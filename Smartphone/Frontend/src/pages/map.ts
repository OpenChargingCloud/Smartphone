import * as L from 'leaflet';

import { api, type ChargingLocation, type EVSE, type LocationCheck, type MapData, type TransactionLimit } from '../api/client';
import { auth } from '../auth';
import { availability, maxPowerKW, positionOf, search, standardWord, stationStatus, statusClass, statusWord, webPaymentOf, webPaymentWord } from '../map/stations';
import { dayAndTime, money } from '../charging/words';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * The map: every charging station, coloured by what it is now, a search above
 * it, and "where am I". A station's pin opens what is known about it, and
 * "Charge now!" at one of its EVSEs that is available starts a session there
 * and goes on to the charging page, where it is watched.
 *
 * The stations are the smartphone's demo stations, or the charging pools of
 * the roaming network whose trust anchor it was given. Those say nothing of
 * their price and nothing that is signed until their pin is opened: then the
 * smartphone checks the proof of each of their stations against the trust
 * anchor, the pin says whether they hold, and only then is there a price and
 * "Charge now!". What the proofs give in full - the tariffs, the energy meters
 * and the keys they sign with - is a page of its own.
 *
 * The map is Leaflet's, drawn into one element of the page that lit-html
 * leaves alone; everything around it is drawn by comparing, as on every other
 * page. Its tiles are OpenStreetMap's - the one thing from elsewhere the page
 * is let load - and its stations are asked again every half minute, so that
 * the pins change colour while somebody looks: every ten seconds while the
 * smartphone follows the status of a roaming network over its WebSocket,
 * which is what tells a free charge point from a taken one.
 */
export const mapPage: Page = {

    title: 'Map',

    render({ root, navigate }) {

        const content = shell(root, {
            active:    '/map',
            title:     'Map',
            subtitle:  'Charging stations, and how they are right now.',
            actions:   reloadButton(() => load(true))
        });

        const mayCharge = auth.can('charging', 'run');

        let cancelled  = false;
        let data:      MapData | null = null;
        let problem:   string  | null = null;
        let query      = '';
        let located:   string  | null = null;
        let locating   = false;
        let timer:     ReturnType<typeof setTimeout> | undefined;

        let map:       L.Map | null = null;
        let you:       L.LayerGroup | null = null;

        const markers  = new Map<string, L.Marker>();
        const popups   = new Map<string, HTMLElement>();
        const chosen   = new Map<string, string>();
        const starting = new Set<string>();
        const failed   = new Map<string, string>();

        // The charge point a driver went to pay for at the web payment page,
        // by its station: the one its live link is handed over at, once the
        // page started charging there and it is no longer free.
        const paying   = new Map<string, string>();
        const checks   = new Map<string, LocationCheck>();
        const checking = new Set<string>();
        const unchecked = new Map<string, string>();

        const ofRoamingNetwork = () => data?.source === 'roamingNetwork';

        /** Of a roaming network, whether the status of its charge points is known: followed over its WebSocket now. */
        const statusKnown = () => !ofRoamingNetwork() || data?.roamingNetwork?.status?.live === true;


        function draw(): void {

            const found = search(data?.locations ?? [], query).slice(0, 8);

            render(content, html`

                <div class="map-page">

                    <div class="map-toolbar">

                        <label class="map-search">
                            <i class="fa-solid fa-magnifying-glass"></i>
                            <input type="search" id="map-search" autocomplete="off"
                                   placeholder="Search a station, a street, a city or an EVSE ID"
                                   aria-label="Search charging stations"
                                   .value=${query}
                                   @input=${(event: Event) => { query = (event.currentTarget as HTMLInputElement).value; draw(); }}
                                   @keydown=${onSearchKey} />
                        </label>

                        <button type="button" id="locate-me" class="btn" ?disabled=${locating} @click=${locateMe}
                                title="Show where this device is on the map">
                            <i class="fa-solid fa-location-crosshairs"></i> ${locating ? 'Locating ...' : 'Locate me'}
                        </button>

                    </div>

                    ${query.trim().length > 0 ? html`
                        <ul class="map-results" aria-label="Stations found">
                            ${found.length === 0
                                  ? html`<li class="muted">No station has "${query.trim()}" in its name, address, operator or EVSE IDs.</li>`
                                  : found.map(location => html`
                                        <li>
                                            <button type="button" class="map-result" @click=${() => focus(location)}>
                                                <span class="station-dot ${statusClass(stationStatus(location))}"></span>
                                                <span class="map-result-name">${location.name}</span>
                                                <span class="muted small">${location.address}, ${location.city} -
                                                    ${!statusKnown()
                                                          ? `${location.evses.length} charge point(s), their status not known`
                                                          : `${availability(location).available} of ${availability(location).total} available`}</span>
                                            </button>
                                        </li>
                                    `)}
                        </ul>
                    ` : nothing}

                    ${problem !== null ? html`<div class="error-box">The charging stations could not be loaded: ${problem}</div>` : nothing}
                    ${data?.roamingNetwork?.error ? html`<div class="error-box">${data.roamingNetwork.error}</div>` : nothing}
                    ${located !== null ? html`<div class="notice">${located}</div>` : nothing}

                    <div id="map" class="map-canvas" role="region" aria-label="Map of charging stations"></div>

                    <div class="map-legend small">
                        ${legend()}
                        ${data !== null ? html`
                            <span class="muted">
                                ${data.source === 'demo'
                                      ? 'Demo stations'
                                      : `From the roaming network ${data.roamingNetwork?.name ?? data.roamingNetwork?.id ?? ''}, its stations checked as they are opened`},
                                as of ${dayAndTime(data.timestamp)}.
                            </span>
                            ${ofRoamingNetwork() ? statusView() : nothing}
                        ` : nothing}
                    </div>

                </div>

            `);

        }


        /** Where the status of the charge points of the roaming network comes from - or why it is not known. */
        function statusView(): TemplateResult {

            const status = data?.roamingNetwork?.status;

            return status?.live === true
                       ? html`<span class="muted roaming-status live">The status of its charge points as it says it now, over its WebSocket since ${dayAndTime(status.since ?? '')} - not signed.</span>`
                       : html`<span class="muted roaming-status not-known">The status of its charge points is not known${status?.error ? html`: ${status.error}` : '.'}</span>`;

        }


        /** The colours a pin can have, and what each says. */
        function legend(): TemplateResult {

            // A roaming network says the status of its EVSEs over its
            // WebSocket: while it is not reached, its pins are of a status
            // not known.
            const shown = !statusKnown()
                              ? [ 'UNKNOWN', 'CHARGING' ] as const
                              : ofRoamingNetwork()
                                    ? [ 'AVAILABLE', 'CHARGING', 'RESERVED', 'INOPERATIVE', 'OUTOFORDER', 'UNKNOWN' ] as const
                                    : [ 'AVAILABLE', 'CHARGING', 'RESERVED', 'OUTOFORDER', 'PLANNED' ] as const;

            return html`${shown.map(status => html`
                <span class="legend-entry"><span class="station-dot ${statusClass(status)}"></span> ${statusWord(status)}</span>
            `)}`;

        }


        /** The map, made once the element it lives in is on the page. */
        function makeMap(): void {

            const element = content.querySelector<HTMLElement>('#map');

            if (element === null || map !== null)
                return;

            map = L.map(element, { zoomControl: true, attributionControl: true }).setView([ 50.9279, 11.5873 ], 12);

            L.tileLayer(data?.tiles ?? 'https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom:      19,
                attribution:  '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors'
            }).addTo(map);

            // Drawn before the page had its size, a map shows one tile in a
            // corner: told once it has.
            requestAnimationFrame(() => map?.invalidateSize());

        }


        /** The pins, made or brought up to date from what the node said. */
        function drawMarkers(fit: boolean): void {

            if (map === null || data === null)
                return;

            const seen   = new Set<string>();
            const bounds = [] as [ number, number ][];

            for (const location of data.locations) {

                const position = positionOf(location);

                if (position === null)
                    continue;

                seen.add(location.id);
                bounds.push(position);

                const icon = L.divIcon({
                                 className:    `station-marker ${statusClass(stationStatus(location))}`,
                                 html:         '<i class="fa-solid fa-charging-station"></i>',
                                 iconSize:     [ 34, 34 ],
                                 iconAnchor:   [ 17, 41 ],
                                 popupAnchor:  [ 0, -38 ]
                             });

                let marker = markers.get(location.id);

                if (marker === undefined) {

                    const element = document.createElement('div');

                    popups.set(location.id, element);

                    const id = location.id;

                    marker = L.marker(position, { icon, title: location.name, alt: location.name, keyboard: true }).
                                 bindPopup(element, { minWidth: 260, maxWidth: 340, autoPanPadding: [ 16, 16 ] }).
                                 on('popupopen', () => void check(id)).
                                 addTo(map);

                    markers.set(location.id, marker);

                }
                else {
                    marker.setLatLng(position);
                    marker.setIcon(icon);
                }

                // Drawn before it is opened, and not as it opens: Leaflet
                // measures a popup as it opens it and moves the map to fit
                // what it measured - an empty one, drawn into afterwards, stood
                // cut off at the top edge of the map.
                drawPopup(location.id);

            }

            for (const [ id, marker ] of markers)
                if (!seen.has(id)) {
                    marker.remove();
                    markers.delete(id);
                    popups.delete(id);
                }

            if (fit && bounds.length > 0)
                map.fitBounds(L.latLngBounds(bounds), { padding: [ 32, 32 ], maxZoom: 14 });

        }


        /** What a station's pin opens: where it is, its EVSEs, and "Charge now!". */
        function drawPopup(id: string): void {

            const location = data?.locations.find(one => one.id === id);
            const element  = popups.get(id);

            if (location === undefined || element === undefined)
                return;

            const free = location.evses.filter(evse => isFree(evse));

            // The EVSE chosen before, while it is still free; otherwise the
            // first free one, so that "Charge now!" means something at once.
            let pick = chosen.get(id);

            if (pick === undefined || !free.some(evse => evse.uid === pick))
                pick = free[0]?.uid;

            const busy     = starting.has(id);
            const signed   = ofRoamingNetwork();
            const checked  = checks.get(id);

            // Of a roaming network, nowhere to charge until its proofs hold:
            // what it costs and who runs it are only what they say then.
            const blocked  = signed && checked?.verified !== true;

            // Where to pay ad hoc, as the proof of the station chosen says -
            // the operator's page, not the QR code at the station.
            const payment  = signed && checked?.verified === true
                                 ? webPaymentOf(checked, location.evses.find(evse => evse.uid === pick)?.evse_id)
                                 : null;

            render(element, html`

                <div class="station-popup">

                    <h3>${location.name}</h3>

                    <div class="small">${location.address}, ${location.postal_code} ${location.city}</div>
                    <div class="small muted">
                        ${location.operator.name} -
                        ${location.tariff !== null
                              ? html`${money(location.tariff.price_per_kwh, location.tariff.currency)} per kWh`
                              : 'the price is what its proof says'}
                    </div>

                    ${signed ? checkView(id, checked) : nothing}

                    <ul class="evse-list">
                        ${location.evses.map(evse => evseView(location, evse, pick, checked))}
                    </ul>

                    ${mayCharge ? html`
                        <button type="button" class="btn primary charge-now"
                                ?disabled=${pick === undefined || busy || blocked}
                                @click=${() => void chargeNow(location, pick)}>
                            <i class="fa-solid fa-bolt"></i> ${busy ? 'Starting ...' : 'Charge now!'}
                        </button>
                        ${pick === undefined ? html`<div class="small muted">No charge point here is available right now.</div>` : nothing}
                    ` : html`
                        <div class="small muted">${mayButNot('look at the map', 'start charging')}</div>
                    `}

                    ${payment?.url ? html`
                        <a class="btn web-payment" href=${payment.url} target="_blank" rel="noopener noreferrer"
                           @click=${() => { if (pick !== undefined) paying.set(id, pick); }}>
                            <i class="fa-solid fa-credit-card"></i> Pay ad hoc at its operator
                        </a>
                        <div class="small muted">The web payment page ${webPaymentWord(payment.of)}, as its proof says.</div>
                    ` : payment?.error ? html`
                        <div class="small muted web-payment-error">${payment.error}</div>
                    ` : nothing}

                    ${mayCharge && signed && checked?.verified === true && (paying.get(id) ?? pick) !== undefined ? html`
                        <div class="live-link">
                            <label class="small">
                                Paid there? The live link its page gave, for ${paying.get(id) ?? pick}:
                                <input name="liveLink" type="text" autocomplete="off"
                                       placeholder="https://... or its document" ?disabled=${busy}
                                       @keydown=${(event: KeyboardEvent) => { if (event.key === 'Enter') void followLiveLink(location, paying.get(id) ?? pick, (event.target as HTMLElement).closest<HTMLElement>('.live-link')!); }}>
                            </label>
                            <label class="small">
                                Its stop link, where it gave one:
                                <input name="stopLink" type="text" autocomplete="off" placeholder="https://..." ?disabled=${busy}>
                            </label>
                            <label class="small">
                                Its dispute link, where it gave one:
                                <input name="disputeLink" type="text" autocomplete="off" placeholder="https://..." ?disabled=${busy}>
                            </label>
                            <label class="small">
                                The reference of the payment it took, where it gave one:
                                <input name="paymentReference" type="text" autocomplete="off" ?disabled=${busy}>
                            </label>
                            <label class="small">
                                The limit chosen there:
                                <span class="limit-input">
                                    <input name="limitValue" type="number" min="0" step="any" ?disabled=${busy}>
                                    <select name="limitKind" ?disabled=${busy}>
                                        <option value="">no limit</option>
                                        <option value="maxEnergy">kWh</option>
                                        <option value="maxCost">${location.tariff?.currency ?? 'in its currency'}</option>
                                        <option value="maxTime">min</option>
                                        <option value="maxSoC">% state of charge</option>
                                    </select>
                                </span>
                            </label>
                            <button type="button" class="btn" ?disabled=${busy}
                                    @click=${(event: MouseEvent) => void followLiveLink(location, paying.get(id) ?? pick,
                                                                                          (event.currentTarget as HTMLElement).closest<HTMLElement>('.live-link')!)}>
                                <i class="fa-solid fa-tower-broadcast"></i> Follow it
                            </button>
                        </div>
                    ` : nothing}

                    ${signed ? html`
                        <a class="small station-details" href="${toURL(`/map/locations/${encodeURIComponent(id)}`)}">
                            <i class="fa-solid fa-file-signature"></i> What is signed about it
                        </a>
                    ` : nothing}

                    ${failed.has(id) ? html`<div class="form-error" role="alert">${failed.get(id)}</div>` : nothing}

                </div>

            `);

            // Open, it is measured anew: what was drawn into it may be taller
            // or shorter than what was there.
            const popup = markers.get(id)?.getPopup();

            if (popup?.isOpen())
                popup.update();

        }


        /** Whether this phone may start at an EVSE: available, or of a roaming network whose status is not known, as at one a driver stands in front of. */
        function isFree(evse: EVSE): boolean {
            return evse.status === 'AVAILABLE' || (ofRoamingNetwork() && evse.status === 'UNKNOWN');
        }


        /** Whether the proofs of a station of the roaming network hold, as checked when its pin was opened. */
        function checkView(id: string, checked: LocationCheck | undefined): TemplateResult | typeof nothing {

            if (checking.has(id) && checked === undefined)
                return html`<div class="station-check small muted"><i class="fa-solid fa-spinner fa-spin"></i> Checking what is signed ...</div>`;

            if (unchecked.has(id))
                return html`<div class="station-check small"><span class="chip bad">not checked</span> ${unchecked.get(id)}</div>`;

            if (checked === undefined)
                return nothing;

            const first = checked.stations.find(station => station.verified);
            const wrong = checked.stations.find(station => !station.verified);

            return checked.verified
                       ? html`<div class="station-check small">
                                  <span class="chip ok"><i class="fa-solid fa-check"></i> signed</span>
                                  attested by ${first?.roamingNetwork?.operator ?? first?.attestation?.signer ?? 'its operator'}
                                  of ${first?.roamingNetwork?.name ?? first?.roamingNetwork?.id ?? 'the roaming network'}
                              </div>`
                       : html`<div class="station-check small">
                                  <span class="chip bad"><i class="fa-solid fa-xmark"></i> not signed</span>
                                  ${wrong?.error ?? 'Its proofs do not hold.'}
                              </div>`;

        }


        /** Check the stations of a pool of the roaming network, as its pin opens - once, until the map is reloaded. */
        async function check(id: string): Promise<void> {

            if (!ofRoamingNetwork() || checking.has(id) || checks.has(id))
                return;

            checking.add(id);
            unchecked.delete(id);
            drawPopup(id);

            try
            {

                const checked = await api.map.check(id);

                if (cancelled)
                    return;

                checks.set(id, checked);

                // What it costs and that it holds are on the map now as well.
                await load();

            }
            catch (error)
            {
                if (!cancelled)
                    unchecked.set(id, errorMessage(error));
            }
            finally
            {
                checking.delete(id);
                if (!cancelled)
                    drawPopup(id);
            }

        }


        function evseView(location: ChargingLocation, evse: EVSE, pick: string | undefined, checked?: LocationCheck): TemplateResult {

            const connector = evse.connectors[0];
            const free      = isFree(evse);
            const price     = checked?.stations.flatMap(station => station.evses ?? []).find(one => one.id === evse.evse_id)?.price;

            return html`
                <li>
                    <label class="evse-choice ${free ? '' : 'unavailable'}">
                        <input type="radio" name="evse-${location.id}" value=${evse.uid}
                               .checked=${evse.uid === pick} ?disabled=${!free || !mayCharge}
                               @change=${() => { chosen.set(location.id, evse.uid); drawPopup(location.id); }} />
                        <span class="evse-status ${statusClass(evse.status)}">${statusWord(evse.status)}</span>
                        <span class="evse-id">${evse.evse_id}</span>
                        <span class="muted small">
                            ${connector !== undefined ? standardWord(connector.standard) : ''} - ${maxPowerKW(evse)} kW
                            ${price ? html` - ${money(price.pricePerKWh, price.currency)}/kWh` : nothing}
                        </span>
                    </label>
                </li>
            `;

        }


        /**
         * Take over the live link the web payment page gave for the session it started at an EVSE - with its stop link, its dispute
         * link, the reference of the payment it took and the limit the driver chose there, where there are - and show the session.
         */
        async function followLiveLink(location: ChargingLocation, evseUid: string | undefined, form: HTMLElement): Promise<void> {

            const liveLink     = form.querySelector<HTMLInputElement>('input[name="liveLink"]')!.value.trim();
            const stopLink     = form.querySelector<HTMLInputElement>('input[name="stopLink"]')?.value.trim()    ?? '';
            const disputeLink  = form.querySelector<HTMLInputElement>('input[name="disputeLink"]')?.value.trim() ?? '';
            const reference    = form.querySelector<HTMLInputElement>('input[name="paymentReference"]')?.value.trim() ?? '';
            const limit        = limitOf(form.querySelector<HTMLSelectElement>('select[name="limitKind"]')?.value ?? '',
                                         Number(form.querySelector<HTMLInputElement>('input[name="limitValue"]')?.value.trim() || Number.NaN));

            if (evseUid === undefined || liveLink === '' || starting.has(location.id))
                return;

            starting.add(location.id);
            failed.delete(location.id);
            drawPopup(location.id);

            try
            {

                const session = await api.charging.follow(location.id, evseUid, liveLink, {
                                        ...(stopLink    !== '' ? { stopLink }    : {}),
                                        ...(disputeLink !== '' ? { disputeLink } : {}),
                                        ...(reference   !== '' ? { paymentReference: reference } : {}),
                                        ...(limit !== undefined ? { limit }      : {})
                                    });

                paying.delete(location.id);

                if (!cancelled)
                    navigate(`/charging?session=${encodeURIComponent(session.id)}`);

            }
            catch (error)
            {
                if (!cancelled)
                    failed.set(location.id, errorMessage(error));
            }
            finally
            {
                starting.delete(location.id);
                if (!cancelled)
                    drawPopup(location.id);
            }

        }


        /**
         * The limit chosen at the web payment page, as OCPP 2.1 says it to the station: of so many kWh in Wh, of so many minutes in
         * seconds, of an amount and of a state of charge as they are - or none, where none was chosen.
         */
        function limitOf(kind: string, value: number): TransactionLimit | undefined {

            if (!(value > 0))
                return undefined;

            switch (kind) {
                case 'maxEnergy':  return { maxEnergy: Math.round(value * 1000) };
                case 'maxCost':    return { maxCost:   value };
                case 'maxTime':    return { maxTime:   Math.round(value * 60) };
                case 'maxSoC':     return { maxSoC:    Math.round(value) };
                default:           return undefined;
            }

        }


        async function chargeNow(location: ChargingLocation, evseUid: string | undefined): Promise<void> {

            if (evseUid === undefined || starting.has(location.id))
                return;

            starting.add(location.id);
            failed.delete(location.id);
            drawPopup(location.id);

            try
            {

                const session = await api.charging.start(location.id, evseUid);

                if (!cancelled)
                    navigate(`/charging?session=${encodeURIComponent(session.id)}`);

            }
            catch (error)
            {

                if (cancelled)
                    return;

                failed.set(location.id, errorMessage(error));

                // Whatever made it fail - somebody else began charging there -
                // is worth seeing at once.
                await load();

            }
            finally
            {
                starting.delete(location.id);
                if (!cancelled)
                    drawPopup(location.id);
            }

        }


        /** Fly to a station the search found, and open its pin. */
        function focus(location: ChargingLocation): void {

            const position = positionOf(location);
            const marker   = markers.get(location.id);

            if (map === null || position === null)
                return;

            // Without the animation: a popup opened while the map still flies
            // is pushed into view against where the map was, and stood cut
            // off at its top edge once it arrived.
            map.setView(position, Math.max(map.getZoom(), 15), { animate: false });
            marker?.openPopup();

        }


        function onSearchKey(event: KeyboardEvent): void {

            if (event.key === 'Enter') {

                event.preventDefault();

                const first = search(data?.locations ?? [], query)[0];

                if (first !== undefined)
                    focus(first);

            }

            if (event.key === 'Escape') {
                query = '';
                draw();
            }

        }


        /** Where this device is, as the browser says it - asked only when the button is pressed. */
        function locateMe(): void {

            if (!window.isSecureContext) {
                located = 'A browser says where it is only to a page served over HTTPS, or from this very machine (127.0.0.1, localhost).';
                draw();
                return;
            }

            if (!('geolocation' in navigator)) {
                located = 'This browser cannot say where it is.';
                draw();
                return;
            }

            locating = true;
            located  = null;
            draw();

            navigator.geolocation.getCurrentPosition(
                position => {

                    if (cancelled || map === null)
                        return;

                    locating = false;

                    const here: [ number, number ] = [ position.coords.latitude, position.coords.longitude ];

                    you?.remove();

                    you = L.layerGroup([
                              L.circle(here, { radius: position.coords.accuracy, className: 'you-are-here-accuracy', interactive: false }),
                              L.circleMarker(here, { radius: 8, className: 'you-are-here' }).bindTooltip('You are here')
                          ]).addTo(map);

                    map.setView(here, Math.max(map.getZoom(), 14));

                    located = `You are here, give or take ${Math.round(position.coords.accuracy)} m.`;
                    draw();

                },
                error => {

                    if (cancelled)
                        return;

                    locating = false;
                    located  = error.code === error.PERMISSION_DENIED     ? 'The browser was not allowed to say where this device is.'
                             : error.code === error.POSITION_UNAVAILABLE  ? 'The browser does not know where this device is.'
                             : error.code === error.TIMEOUT               ? 'The browser did not say where this device is in time.'
                             :                                              `Where this device is could not be found: ${error.message}`;
                    draw();

                },
                { enableHighAccuracy: true, timeout: 10_000, maximumAge: 60_000 }
            );

        }


        async function load(refresh = false): Promise<void> {

            clearTimeout(timer);

            const first = data === null;

            try
            {

                // Reload asks the roaming network again, and checks again what
                // is opened: an attestation holds for minutes.
                const loaded = await api.map.get(refresh);

                if (refresh)
                    checks.clear();

                if (cancelled)
                    return;

                data    = loaded;
                problem = null;

                draw();
                makeMap();
                drawMarkers(first);

            }
            catch (error)
            {

                if (cancelled)
                    return;

                problem = errorMessage(error);

                draw();
                makeMap();

            }

            if (!cancelled)
                timer = setTimeout(() => void load(), ofRoamingNetwork() && statusKnown() ? 10_000 : 30_000);

        }


        draw();
        void load();

        return () => {
            cancelled = true;
            clearTimeout(timer);
            map?.remove();
            map = null;
        };

    }

};
