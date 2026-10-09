import * as L from 'leaflet';

import { api, type ChargingLocation, type EVSE, type MapData } from '../api/client';
import { auth } from '../auth';
import { availability, maxPowerKW, positionOf, search, standardWord, stationStatus, statusClass, statusWord } from '../map/stations';
import { dayAndTime, money } from '../charging/words';
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
 * The map is Leaflet's, drawn into one element of the page that lit-html
 * leaves alone; everything around it is drawn by comparing, as on every other
 * page. Its tiles are OpenStreetMap's - the one thing from elsewhere the page
 * is let load - and its stations are asked again every half minute, so that
 * the pins change colour while somebody looks.
 */
export const mapPage: Page = {

    title: 'Map',

    render({ root, navigate }) {

        const content = shell(root, {
            active:    '/map',
            title:     'Map',
            subtitle:  'Charging stations, and how they are right now.',
            actions:   reloadButton(() => load())
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
                                                    ${availability(location).available} of ${availability(location).total} available</span>
                                            </button>
                                        </li>
                                    `)}
                        </ul>
                    ` : nothing}

                    ${problem !== null ? html`<div class="error-box">The charging stations could not be loaded: ${problem}</div>` : nothing}
                    ${located !== null ? html`<div class="notice">${located}</div>` : nothing}

                    <div id="map" class="map-canvas" role="region" aria-label="Map of charging stations"></div>

                    <div class="map-legend small">
                        ${legend()}
                        ${data !== null ? html`
                            <span class="muted">
                                ${data.source === 'demo' ? 'Demo stations' : `From ${data.stationsURL}`},
                                as of ${dayAndTime(data.timestamp)}.
                            </span>
                        ` : nothing}
                    </div>

                </div>

            `);

        }


        /** The colours a pin can have, and what each says. */
        function legend(): TemplateResult {

            const shown = [ 'AVAILABLE', 'CHARGING', 'RESERVED', 'OUTOFORDER', 'PLANNED' ] as const;

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

                    marker = L.marker(position, { icon, title: location.name, alt: location.name, keyboard: true }).
                                 bindPopup(element, { minWidth: 260, maxWidth: 340, autoPanPadding: [ 16, 16 ] }).
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

            const free = location.evses.filter(evse => evse.status === 'AVAILABLE');

            // The EVSE chosen before, while it is still free; otherwise the
            // first free one, so that "Charge now!" means something at once.
            let pick = chosen.get(id);

            if (pick === undefined || !free.some(evse => evse.uid === pick))
                pick = free[0]?.uid;

            const busy = starting.has(id);

            render(element, html`

                <div class="station-popup">

                    <h3>${location.name}</h3>

                    <div class="small">${location.address}, ${location.postal_code} ${location.city}</div>
                    <div class="small muted">
                        ${location.operator.name} - ${money(location.tariff.price_per_kwh, location.tariff.currency)} per kWh
                    </div>

                    <ul class="evse-list">
                        ${location.evses.map(evse => evseView(location, evse, pick))}
                    </ul>

                    ${mayCharge ? html`
                        <button type="button" class="btn primary charge-now"
                                ?disabled=${pick === undefined || busy}
                                @click=${() => void chargeNow(location, pick)}>
                            <i class="fa-solid fa-bolt"></i> ${busy ? 'Starting ...' : 'Charge now!'}
                        </button>
                        ${pick === undefined ? html`<div class="small muted">No charge point here is available right now.</div>` : nothing}
                    ` : html`
                        <div class="small muted">${mayButNot('look at the map', 'start charging')}</div>
                    `}

                    ${failed.has(id) ? html`<div class="form-error" role="alert">${failed.get(id)}</div>` : nothing}

                </div>

            `);

            // Open, it is measured anew: what was drawn into it may be taller
            // or shorter than what was there.
            const popup = markers.get(id)?.getPopup();

            if (popup?.isOpen())
                popup.update();

        }


        function evseView(location: ChargingLocation, evse: EVSE, pick: string | undefined): TemplateResult {

            const connector = evse.connectors[0];
            const free      = evse.status === 'AVAILABLE';

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
                        </span>
                    </label>
                </li>
            `;

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


        async function load(): Promise<void> {

            clearTimeout(timer);

            const first = data === null;

            try
            {

                const loaded = await api.map.get();

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
                timer = setTimeout(() => void load(), 30_000);

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
