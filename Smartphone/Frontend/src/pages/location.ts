import { api, type CheckedEVSE, type CheckedPriceStatement, type CheckedTariff, type EnergyMeter, type LocationCheck, type StationCheck, type TariffElement } from '../api/client';
import { dayAndTime, money } from '../charging/words';
import { standardWord } from '../map/stations';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * What is signed about a charging pool of the roaming network: the proof of
 * each of its stations, checked against the trust anchor the smartphone was
 * given, by whom it is attested and until when - and, where it holds,
 * everything it gives: each EVSE with its tariffs, the price a price
 * statement sets, and its energy meter with the public keys it signs its
 * measurements with, so that it is known before the first meter value which
 * key checks it. Where a proof does not hold, why - and nothing of what it
 * says.
 */
export const locationPage: Page = {

    title: params => `Charging station ${params['id'] ?? ''}`.trim(),

    render({ root, params, url }) {

        // From the route - and from the address where a page is opened
        // without one, as a test opens it.
        const id = params['id'] ?? decodeURIComponent(url.pathname.split('/').pop() ?? '');

        const content = shell(root, {
            active:    '/map',
            title:     'What is signed',
            subtitle:  id,
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Checking what is signed ...</div>`);

        let cancelled = false;


        function kv(Key: string, Value: TemplateResult | string | null | undefined): TemplateResult {
            return html`<div class="kv"><span class="k">${Key}</span><span class="v">${Value ?? '-'}</span></div>`;
        }

        function verdict(verified: boolean): TemplateResult {
            return verified
                       ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> signed</span>`
                       : html`<span class="chip bad"><i class="fa-solid fa-xmark"></i> not signed</span>`;
        }


        function draw(check: LocationCheck): void {

            const first = check.stations.find(station => station.verified);

            render(content, html`

                <p class="small"><a href="${toURL('/map')}"><i class="fa-solid fa-arrow-left"></i> The map</a></p>

                <div class="cards stacked">

                    <section class="card">

                        <h2><i class="fa-solid fa-file-signature"></i> ${check.name} ${verdict(check.verified)}</h2>

                        <p class="small">
                            ${check.verified
                                  ? html`Every station here is in a proof the operator of the roaming network attested, as the trust
                                         anchor this smartphone was given names it. What follows is what those proofs say - nothing
                                         else, and none of it from the list the map was drawn from.`
                                  : html`Not every station here is in a proof attested by the operator the trust anchor names.
                                         Of those that are not, nothing is shown, and nothing is charged there.`}
                        </p>

                        ${first?.roamingNetwork ? html`
                            <div class="kv-list">
                                ${kv('Roaming network',  `${first.roamingNetwork.name ?? first.roamingNetwork.id ?? ''} (${first.roamingNetwork.id ?? ''})`)}
                                ${kv('Its operator',     first.roamingNetwork.operator)}
                                ${kv('Revision',         first.roamingNetwork.revision?.toString())}
                                ${kv('Commit',           html`<span class="commit">${first.roamingNetwork.commitId ?? '-'}</span>`)}
                            </div>
                        ` : nothing}

                        ${first?.textMismatch ? html`
                            <div class="notice">The trust anchor names this roaming network otherwise than its proof: ${first.textMismatch}</div>
                        ` : nothing}

                        <p class="small muted">Checked ${dayAndTime(check.checkedAt)}.</p>

                    </section>

                    ${check.stations.map(station => stationView(station))}

                </div>

            `);

        }


        function stationView(station: StationCheck): TemplateResult {

            if (!station.verified)
                return html`
                    <section class="card station-check-card">
                        <h2><i class="fa-solid fa-charging-station"></i> ${station.stationId} ${verdict(false)}</h2>
                        <div class="error-box">${station.error}</div>
                        ${station.from ? html`<p class="small muted">Asked at ${station.from}.</p>` : nothing}
                    </section>
                `;

            const attestation  = station.attestation;
            const made         = [ station.station?.manufacturer, station.station?.model ].filter(part => part).join(' ');

            return html`
                <section class="card station-check-card">

                    <h2><i class="fa-solid fa-charging-station"></i> ${station.stationId} ${verdict(true)}</h2>

                    <div class="kv-list">
                        ${kv('Operator',        `${station.chargingStationOperator?.name ?? ''} (${station.chargingStationOperator?.id ?? ''})`)}
                        ${made.length > 0 ? kv('Station', made) : nothing}
                        ${station.station?.firmwareVersion ? kv('Firmware', station.station.firmwareVersion) : nothing}
                        ${attestation ? html`
                            ${kv('Attested by',  `${attestation.signer ?? station.roamingNetwork?.operator ?? ''}, as ${attestation.role}`)}
                            ${kv('Its key',      html`<span class="commit">${attestation.keyId}</span> (${attestation.algorithm})`)}
                            ${kv('Signed',       dayAndTime(attestation.signedAt))}
                            ${kv('Valid until',  dayAndTime(attestation.validUntil))}
                        ` : nothing}
                        ${kv('Served by',       station.from)}
                    </div>

                    ${(station.evses ?? []).map(evse => evseView(evse))}

                    ${priceStatementsView(station)}

                </section>
            `;

        }


        function evseView(evse: CheckedEVSE): TemplateResult {

            const connectors = evse.connectors.map(connector => `${standardWord(connector.type ?? '')}${connector.format ? ` (${connector.format.toLowerCase()})` : ''}`).join(', ');

            return html`
                <div class="checked-evse">

                    <h3><span class="evse-id">${evse.id}</span>
                        <span class="muted small">${evse.maxPowerKW !== null ? `${evse.maxPowerKW} kW` : ''} ${connectors}</span></h3>

                    <div class="kv-list">
                        ${kv('Price',    evse.price ? `${money(evse.price.pricePerKWh, evse.price.currency)} per kWh` : 'none named per kWh')}
                        ${evse.tariffs.map(tariff => kv(`Tariff ${tariff.id}`, tariffView(tariff)))}
                    </div>

                    ${evse.energyMeter ? meterView(evse.energyMeter) : html`<p class="small muted">The proof names no energy meter of this EVSE.</p>`}

                </div>
            `;

        }


        function tariffView(tariff: CheckedTariff): TemplateResult | string {

            if (tariff.missing)
                return 'named, and not in the proof';

            return html`${tariff.name ? html`${tariff.name}: ` : nothing}${elementsView(tariff.elements ?? [], tariff.currency ?? '')}`;

        }


        function elementsView(elements: TariffElement[], currency: string): string {

            return elements.map(element => element.priceComponents.map(component =>
                       component.type === 'ENERGY'        ? `${money(component.price, currency)}/kWh`
                     : component.type === 'TIME'          ? `${money(component.price, currency)}/h`
                     : component.type === 'PARKING_TIME'  ? `${money(component.price, currency)}/h parking`
                     : component.type === 'FLAT'          ? `${money(component.price, currency)} once`
                     :                                      `${money(component.price, currency)} ${component.type.toLowerCase()}`
                   ).join(' + ') + (element.restrictions ? ' (restricted)' : '')).join('; ');

        }


        function meterView(meter: EnergyMeter): TemplateResult {

            const made = [ meter.manufacturer, meter.model ].filter(part => part).join(' ');

            return html`
                <div class="checked-meter">

                    <h4><i class="fa-solid fa-gauge-high"></i> Energy meter ${meter.id ?? ''}</h4>

                    <div class="kv-list">
                        ${made.length > 0 ? kv('Meter', made) : nothing}
                        ${kv('Serial number',  meter.serialNumber)}
                        ${meter.firmwareVersion ? kv('Firmware', meter.firmwareVersion) : nothing}
                        ${kv('Signs in',       meter.signatureMethods.join(', '))}
                    </div>

                    <p class="small">
                        What it signs its meter values with: a transparency software checks every one of them against
                        ${meter.publicKeys.length === 1 ? 'this key' : 'one of these keys'}, and against no key a meter
                        value names of itself.
                    </p>

                    ${meter.publicKeys.map(key => html`
                        <div class="meter-key">
                            <div class="small">
                                ${key.error
                                      ? html`<span class="chip bad">not a key</span> ${key.error}`
                                      : html`<span class="chip">${key.algorithm}</span>
                                             ${key.certificateChain ? html`<span class="muted">with ${key.certificateChain} certificate(s)</span>` : nothing}`}
                            </div>
                            <div class="small muted">SHA-256: <span class="commit">${key.id}</span></div>
                            <code class="commit public-key">${key.key}</code>
                        </div>
                    `)}

                </div>
            `;

        }


        function priceStatementsView(station: StationCheck): TemplateResult | typeof nothing {

            const statements = station.priceStatements ?? [];

            if (statements.length === 0 && !station.priceStatementsError)
                return html`<p class="small muted">No price statement sets another price here: the tariffs apply as they are.</p>`;

            return html`
                <h3>Price statements</h3>
                ${station.priceStatementsError ? html`<div class="error-box">${station.priceStatementsError}</div>` : nothing}
                <ul class="price-statements">
                    ${statements.map(statement => statementView(statement, station))}
                </ul>
            `;

        }


        function statementView(statement: CheckedPriceStatement, station: StationCheck): TemplateResult {

            // The currency of a price is the one of its tariff.
            const currencyOf = (tariffId: string) => (station.evses ?? []).flatMap(evse => evse.tariffs).find(tariff => tariff.id === tariffId)?.currency ?? '';

            return html`
                <li>
                    ${statement.verified
                          ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> holds</span>`
                          : html`<span class="chip bad"><i class="fa-solid fa-xmark"></i> does not hold</span>`}
                    <span class="evse-id">${statement.entityId}</span>
                    ${(statement.prices ?? []).map(prices => html`<span>${prices.chargingTariffId}: ${elementsView(prices.elements, currencyOf(prices.chargingTariffId))}</span>`)}
                    <div class="small muted">
                        by ${statement.signer ?? '?'}, number ${statement.sequence ?? '?'}, signed ${statement.signedAt ? dayAndTime(statement.signedAt) : '?'},
                        ${statement.validUntil ? `valid until ${dayAndTime(statement.validUntil)}` : 'valid until the next one'}
                    </div>
                    ${statement.error ? html`<div class="small form-error">${statement.error}</div>` : nothing}
                </li>
            `;

        }


        async function load(): Promise<void> {

            try
            {
                const check = await api.map.check(id);

                if (!cancelled)
                    draw(check);
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`
                        <p class="small"><a href="${toURL('/map')}"><i class="fa-solid fa-arrow-left"></i> The map</a></p>
                        <div class="error-box">What is signed about ${id} could not be checked: ${errorMessage(problem)}</div>
                    `);
            }

        }

        void load();

        return () => { cancelled = true; };

    }

};
