import { api, type ChargingSession, type LiveLinkEnding, type LiveLinkFollow, type LiveLinkStartCheck, type LiveLinkValueCheck, type LiveLinkWrongValue, type MeterValueCheck, type SessionCheck } from '../api/client';
import { auth } from '../auth';
import { dayAndTime, duration, isRunning, limitFigure, money, reportedInWords, sessionStatusClass, sessionStatusWord, stationStopInWords, withUnit } from '../charging/words';
import { standardWord } from '../map/stations';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * One charging session, with everything there is to know about it: where,
 * at which charge point, from when to when, what it charged and what it
 * cost - and, while it still starts or charges, its figures asked for again
 * every two seconds and Stop - of one the web payment page of its operator
 * started, where the page gave a stop link. A session the e-mobility
 * provider reports has no power, no target and no time it is full at: OCPI
 * tells none of them.
 * One at a station of a roaming network says how it holds against what it
 * kept of the station: whether the meter signed the values of its charge
 * detail record, and the price the operator stated for its start.
 */
export const sessionPage: Page = {

    title: params => `Charging session ${params['id'] ?? ''}`.trim(),

    render({ root, params, url }) {

        // From the route - and from the address where a page is opened
        // without one, as a test opens it.
        const id = params['id'] ?? decodeURIComponent(url.pathname.split('/').pop() ?? '');

        const content = shell(root, {
            active:    '/charging/history',
            title:     'Charging session',
            subtitle:  id,
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayStop = auth.can('charging', 'run');

        let cancelled  = false;
        let stopping   = false;
        let failed:    string | null = null;
        let timer:     ReturnType<typeof setTimeout> | undefined;


        function kv(Key: string, Value: TemplateResult | string): TemplateResult {
            return html`<div class="kv"><span class="k">${Key}</span><span class="v">${Value}</span></div>`;
        }


        function matches(Matches: boolean | null): TemplateResult | typeof nothing {
            return Matches === null
                       ? nothing
                       : Matches
                             ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> the same</span>`
                             : html`<span class="chip bad"><i class="fa-solid fa-xmark"></i> not the same</span>`;
        }


        /** How the session holds against what it kept of the station of the roaming network. */
        function checkView(check: SessionCheck, session: ChargingSession): TemplateResult {

            const values  = check.meterValues;
            const price   = check.price;

            return html`
                <section class="card session-check">

                    <h2><i class="fa-solid fa-shield-halved"></i> Checked</h2>

                    ${check.error !== null ? html`<div class="error-box check-error">${check.error}</div>` : nothing}

                    ${check.error === null && values === undefined ? html`
                        <p class="small muted meter-values-pending">
                            The meter values are checked once the charge detail record came, with the key of the
                            energy meter the station's proof gave when they were measured.
                        </p>
                    ` : nothing}

                    ${values !== undefined ? html`
                        <h3>
                            Meter values
                            ${values.verified
                                  ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> signed by the meter</span>`
                                  : html`<span class="chip bad"><i class="fa-solid fa-xmark"></i> not all signed by the meter</span>`}
                        </h3>
                        ${values.error !== null ? html`<div class="error-box meter-values-error">${values.error}</div>` : nothing}
                        <div class="kv-list">
                            ${values.energyKWh !== null && values.startKWh !== null && values.endKWh !== null
                                  ? kv('Signed', `${withUnit(values.startKWh, 'kWh', 3)} to ${withUnit(values.endKWh, 'kWh', 3)}, ${withUnit(values.energyKWh, 'kWh', 3)} in all`)
                                  : kv('Signed', 'no begin and end the meter signed')}
                            ${values.cdrEnergyKWh !== null
                                  ? kv('The record says', html`${withUnit(values.cdrEnergyKWh, 'kWh', 3)} ${matches(values.energyMatches)}`)
                                  : nothing}
                            ${values.expectedCost !== null && price?.selected
                                  ? kv('At the price', html`${money(values.expectedCost, price.selected.currency)}
                                                            ${values.cdrCost !== null ? html`<span class="muted">- the record says ${money(values.cdrCost, price.selected.currency)}</span>` : nothing}
                                                            ${matches(values.costMatches)}`)
                                  : nothing}
                        </div>
                        <ul class="meter-values">
                            ${values.values.map(value => meterValueView(value))}
                        </ul>
                        <p class="small muted">
                            Each checked with the key the station's proof gave for the meter of ${session.evse.evseId} at
                            the time the meter signed - not a key the record names itself. That the meter's clock was
                            right is not proven.
                        </p>
                    ` : nothing}

                    ${price !== undefined ? html`
                        <h3>Price</h3>
                        <div class="session-price">
                            ${price.error !== null
                                  ? html`<div class="error-box price-error">${price.error}</div>`
                                  : price.selected !== null
                                        ? html`
                                              <div class="kv-list">
                                                  ${kv('Stated', html`${price.selected.pricePerKWh !== null
                                                                             ? `${money(price.selected.pricePerKWh, price.selected.currency)} per kWh`
                                                                             : 'no price per kWh'}
                                                                       ${price.matchesSession === false
                                                                             ? html`<span class="chip bad">this phone counted with ${money(session.cost.pricePerKWh, session.cost.currency)}</span>`
                                                                             : nothing}`)}
                                                  ${kv('By',       `${price.selected.signer}, for ${price.selected.entityType} ${price.selected.entityId}, number ${price.selected.sequence}, signed ${dayAndTime(price.selected.signedAt)}`)}
                                                  ${price.selected.tariffId !== null ? kv('Tariff', html`<span class="mono">${price.selected.tariffId}</span>`) : nothing}
                                              </div>
                                              <p class="small muted">
                                                  ${price.applicability === 'bound'
                                                        ? 'In force when it started, and assigned to this session by the operator.'
                                                        : 'In force for this charge point when it started. The operator signed no assignment of it to this session.'}
                                              </p>`
                                        : html`<p class="small muted price-none">
                                                   ${price.signatures === 'missing'
                                                         ? 'The operator stated no price for this charge point: what it costs is the tariff the station\'s proof gives.'
                                                         : (price.diagnostics[0] ?? 'No price statement was in force when it started.')}
                                               </p>`}
                        </div>
                    ` : nothing}

                    <p class="small muted">Checked ${dayAndTime(check.checkedAt)}, under the identity keys of the roaming network trusted now.</p>

                </section>
            `;

        }


        /** The live link of a session the web payment page of its operator started: how it is followed, and what came over it. */
        function liveLinkView(link: LiveLinkFollow): TemplateResult {

            return html`
                <section class="card session-live-link">

                    <h2><i class="fa-solid fa-tower-broadcast"></i> Live link</h2>

                    <div class="kv-list">
                        ${kv('Handed over',       dayAndTime(link.handedOverAt))}
                        ${kv('From',              link.from !== null ? html`<span class="mono">${link.from}</span>` : 'its document itself')}
                        ${kv('Followed over',     link.transports.length > 0 ? link.transports.join(', ') : 'nothing')}
                        ${kv('Updates',           link.lastUpdateAt !== null
                                                      ? `${link.updates}, the last at ${dayAndTime(link.lastUpdateAt)}${link.lastTransport !== null ? ` over ${link.lastTransport}` : ''}`
                                                      : 'none yet')}
                        ${link.lastEndpoint !== null ? kv('Last from', html`<span class="mono">${link.lastEndpoint}</span>`) : nothing}
                        ${kv('Signed documents',  String(link.documents))}
                        ${link.problem      !== null ? kv('Now',   link.problem)      : nothing}
                        ${link.endedBecause !== null ? kv('Ended', link.endedBecause) : nothing}
                        ${kv('Stop link',         link.stopLink !== null ? html`at <span class="mono">${link.stopLink}</span>` : 'none given: it is stopped at the page')}
                        ${link.stop !== null ? kv('Stop asked', html`${dayAndTime(link.stop.requestedAt)} by ${link.stop.requestedBy}:
                                                                     <span class="${link.stop.accepted ? 'stop-taken' : 'form-error'}">${link.stop.accepted ? 'taken' : 'not taken'} - ${link.stop.answer}</span>`) : nothing}
                    </div>

                    ${link.limits.length > 0 ? html`
                        <ul class="live-limits">
                            ${link.limits.map(limit => html`
                                <li class="${limit.reached === true ? 'reached' : limit.reached === false ? 'not-reached' : 'unknown'}">
                                    ${limit.reached === true
                                          ? html`<i class="fa-solid fa-flag-checkered" title="reached"></i>`
                                          : limit.reached === false
                                                ? html`<i class="fa-regular fa-flag" title="not reached"></i>`
                                                : html`<i class="fa-solid fa-question" title="its signed values do not say"></i>`}
                                    <span>Limit ${limitFigure(limit, limit.limit)}</span>
                                    <span class="muted">${limit.value !== null ? limitFigure(limit, limit.value) : 'not measured by the meter'}</span>
                                </li>
                            `)}
                        </ul>
                    ` : nothing}

                    ${link.ending !== null ? endingView(link.ending) : nothing}

                    ${startView(link, link.start)}

                    ${link.wrong !== null ? wrongView(link, link.wrong) : nothing}

                    ${link.readings.length > 0 ? html`
                        <ul class="live-readings">
                            ${link.readings.map(reading => html`
                                <li class="${reading.holds === true ? 'verified' : reading.holds === false ? 'refused' : 'unchecked'}">
                                    ${reading.holds === true
                                          ? html`<i class="fa-solid fa-check" title="the meter's"></i>`
                                          : reading.holds === false
                                                ? html`<i class="fa-solid fa-xmark" title="a wrong one"></i>`
                                                : html`<i class="fa-solid fa-minus" title="not checked"></i>`}
                                    <span>${dayAndTime(reading.measuredAt)}</span>
                                    <span>${reading.energyKWh !== null ? withUnit(reading.energyKWh, 'kWh', 3) : 'no energy register'}</span>
                                    ${reading.transaction !== null ? html`<span class="muted">${reading.transaction}</span>` : nothing}
                                </li>
                            `)}
                        </ul>
                    ` : nothing}

                    <p class="small muted">
                        A key a live link names is no authority, the one the proof of the station gives for the meter
                        is: every signed value is checked with it as it comes, and against the values before it - none
                        measured before the start, the meter counting forwards. A wrong one stops the session here, and
                        the station through the stop link of the page, where it gave one. At the end, what the signed
                        values say is compared with the limit chosen at the page; the end value itself is not checked
                        yet. The documents are kept, each as it came.
                    </p>

                </section>
            `;

        }


        /** How a session followed over its live link ended, against the limit chosen at the page. */
        function endingView(ending: LiveLinkEnding): TemplateResult {

            return html`
                <div class="live-ending ${ending.reason}">
                    ${ending.reason === 'stopped'
                          ? html`<span class="chip"><i class="fa-solid fa-stop"></i> stopped by this phone</span>`
                          : ending.reason === 'limit'
                                ? html`<span class="chip ok"><i class="fa-solid fa-flag-checkered"></i> at its limit</span>`
                                : ending.reason === 'unknown'
                                      ? html`<span class="chip"><i class="fa-solid fa-question"></i> its limit unknown</span>`
                                      : html`<span class="chip"><i class="fa-solid fa-circle-info"></i> another reason</span>`}
                    <span>${ending.because}</span>
                </div>
            `;

        }


        function startView(link: LiveLinkFollow, start: LiveLinkStartCheck | null): TemplateResult {

            if (start === null)
                return html`<p class="live-start pending">No signed start value came over it yet.</p>`;

            const reading = start.reading;

            return html`
                <div class="live-start ${start.holds === true ? 'verified' : start.holds === false ? 'refused' : 'unchecked'}">
                    ${start.holds === true
                          ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> the meter's</span>`
                          : start.holds === false
                                ? html`<span class="chip bad"><i class="fa-solid fa-xmark"></i> a wrong start value</span>`
                                : html`<span class="chip"><i class="fa-solid fa-question"></i> not checked</span>`}
                    <span>Start value</span>
                    <span>${dayAndTime(reading.measuredAt)}${reading.energyKWh !== null ? `: ${withUnit(reading.energyKWh, 'kWh', 3)}` : ''}</span>
                    ${checkedWith(start)}
                    ${start.error !== null ? html`<div class="small">${start.error}</div>` : nothing}
                    ${start.holds === false ? html`
                        <div class="error-box">
                            This phone stopped the session: its signed start meter value is a wrong one. ${stationStopInWords(link)}
                        </div>
                    ` : nothing}
                </div>
            `;

        }


        function wrongView(link: LiveLinkFollow, wrong: LiveLinkWrongValue): TemplateResult {

            return html`
                <div class="live-wrong">
                    <span class="chip bad"><i class="fa-solid fa-xmark"></i> a wrong meter value</span>
                    <span>After the start</span>
                    ${wrong.readings.map(reading => html`
                        <span>${dayAndTime(reading.measuredAt)}${reading.energyKWh !== null ? `: ${withUnit(reading.energyKWh, 'kWh', 3)}` : ''}</span>
                    `)}
                    ${checkedWith(wrong)}
                    ${wrong.error !== null ? html`<div class="small">${wrong.error}</div>` : nothing}
                    <div class="error-box">
                        This phone stopped the session: a signed meter value after its start is a wrong one. ${stationStopInWords(link)}
                    </div>
                </div>
            `;

        }


        /** The meter and its key a signed value of a live link was checked with, and when. */
        function checkedWith(check: LiveLinkValueCheck): TemplateResult {

            return html`
                <div class="small muted">
                    ${check.meterSerialNumber !== null ? `meter ${check.meterSerialNumber}, ` : ''}${check.keyId !== null ? html`key <span class="commit">${check.keyId}</span>, ` : nothing}checked ${dayAndTime(check.checkedAt)}
                </div>
            `;

        }


        function meterValueView(value: MeterValueCheck): TemplateResult {

            return html`
                <li class="meter-value ${value.verified ? 'verified' : 'not-verified'}">
                    ${value.verified
                          ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> the meter's</span>`
                          : html`<span class="chip bad"><i class="fa-solid fa-xmark"></i> ${value.result === null ? 'not checked' : 'not the meter\'s'}</span>`}
                    <span>${value.nature ?? 'Meter value'}</span>
                    ${value.readings.map(reading => html`
                        <span>${dayAndTime(reading.measuredAt)}${reading.energyKWh !== null ? `: ${withUnit(reading.energyKWh, 'kWh', 3)}` : ''}</span>
                    `)}
                    ${value.meterSerialNumber !== null || value.keyId !== null ? html`
                        <div class="small muted">
                            ${value.meterSerialNumber !== null ? `meter ${value.meterSerialNumber}` : ''}
                            ${value.keyId !== null ? html`, key <span class="commit">${value.keyId}</span>` : nothing}
                            ${value.ofThisStart ? ', identified by this start' : nothing}
                        </div>
                    ` : nothing}
                    ${value.error !== null ? html`<div class="small form-error">${value.error}</div>` : nothing}
                </li>
            `;

        }


        function draw(session: ChargingSession): void {

            const charging  = session.status === 'charging';
            const running   = isRunning(session);

            render(content, html`

                <p class="small"><a href="${toURL('/charging/history')}"><i class="fa-solid fa-arrow-left"></i> Every session</a></p>

                <div class="cards">

                    <section class="card">

                        <h2>
                            <i class="fa-solid fa-bolt"></i> ${session.location.name}
                            <span class="session-status ${sessionStatusClass(session.status)}">${sessionStatusWord(session.status)}</span>
                        </h2>

                        <div class="kv-list">
                            ${kv('Energy',     withUnit(session.energyKWh, 'kWh', 3))}
                            ${session.powerKW !== undefined
                                  ? kv('Power', charging ? withUnit(session.powerKW, 'kW') : 'none - it is over')
                                  : nothing}
                            ${kv('Duration',   duration(session.durationSeconds))}
                            ${kv('Cost',       html`${money(session.cost.amount, session.cost.currency)}
                                                    <span class="muted">${session.cost.reported === true
                                                                              ? '(as the operator reports it)'
                                                                              : `(${money(session.cost.pricePerKWh, session.cost.currency)} per kWh)`}</span>`)}
                            ${session.progressPercent !== undefined && session.targetEnergyKWh !== undefined
                                  ? kv('Progress', html`${withUnit(session.progressPercent, '%')} of ${withUnit(session.targetEnergyKWh, 'kWh')}`)
                                  : nothing}
                        </div>

                        ${charging && session.progressPercent !== undefined ? html`
                            <progress class="session-progress" max="100" .value=${session.progressPercent}
                                      aria-label="How far it has charged"></progress>
                        ` : nothing}

                        ${running && mayStop && (session.liveLink === undefined || session.liveLink.stopLink !== null) ? html`
                            <div class="form-actions">
                                <button type="button" class="btn danger" ?disabled=${stopping} @click=${() => void stop()}>
                                    <i class="fa-solid fa-stop"></i> ${stopping ? 'Stopping ...' : session.liveLink?.stop?.accepted === true ? 'Stop again' : 'Stop charging'}
                                </button>
                                ${failed !== null ? html`<span class="form-error" role="alert">${failed}</span>` : nothing}
                            </div>
                        ` : nothing}

                    </section>

                    <section class="card">

                        <h2><i class="fa-solid fa-charging-station"></i> Where</h2>

                        <div class="kv-list">
                            ${kv('Station',       session.location.name)}
                            ${kv('Address',       `${session.location.address}, ${session.location.city}`)}
                            ${kv('Operator',      session.location.operator)}
                            ${kv('Charge point',  html`<span class="mono">${session.evse.evseId}</span>`)}
                            ${kv('Connector',     `${standardWord(session.evse.standard)}, ${session.evse.powerType === 'DC' ? 'DC' : 'AC'}, up to ${withUnit(session.evse.maxPowerKW, 'kW', 0)}`)}
                        </div>

                    </section>

                    <section class="card">

                        <h2><i class="fa-solid fa-clock"></i> When</h2>

                        <div class="kv-list">
                            ${kv('Started',     dayAndTime(session.startedAt))}
                            ${kv('Started by',  session.startedBy)}
                            ${session.chargingSince !== undefined ? kv('Charging since', dayAndTime(session.chargingSince)) : nothing}
                            ${running
                                  ? session.fullAt !== undefined ? kv('Full at', dayAndTime(session.fullAt)) : nothing
                                  : kv('Ended', dayAndTime(session.endedAt ?? session.fullAt ?? ''))}
                            ${session.stoppedBy !== undefined ? kv('Stopped by', session.stoppedBy) : nothing}
                            ${kv('Session',     html`<span class="mono">${session.id}</span>`)}
                        </div>

                        ${!session.simulated && (session.provider !== undefined || session.liveLink !== undefined) ? html`
                            <p class="small muted session-reported">${reportedInWords(session)}</p>
                        ` : nothing}

                        ${session.simulated ? html`
                            <p class="small muted">
                                ${session.start !== undefined
                                      ? 'Started through the e-mobility provider, which countersigned the start. What it charged is simulated: the provider reports nothing of it.'
                                      : `Simulated: ${session.ticket !== undefined
                                                          ? 'the e-mobility provider issued its ticket, and took no start request'
                                                          : 'no e-mobility provider was asked'}, and nothing was charged for real.`}
                            </p>
                        ` : nothing}

                    </section>

                    ${session.liveLink !== undefined ? liveLinkView(session.liveLink) : nothing}

                    ${session.ticket !== undefined ? html`
                        <section class="card session-ticket">

                            <h2><i class="fa-solid fa-ticket"></i> Charging ticket</h2>

                            <div class="kv-list">
                                ${kv('Ticket',        html`<span class="commit">${session.ticket.id}</span>`)}
                                ${kv('Provider',      session.ticket.party)}
                                ${kv('Good',          `${dayAndTime(session.ticket.notBefore)} until ${dayAndTime(session.ticket.notAfter)}`)}
                                ${kv('Its key',       html`<span class="commit">${session.ticket.keyThumbprint}</span>`)}
                                ${kv('Signed by',     html`<span class="commit">${session.ticket.issuerFingerprint}</span>`)}
                            </div>

                            <p class="small muted">
                                A ticket of this start alone, with a key of its own, signed by the provider's ticket issuer
                                and not by this phone's account: a charge point operator learns who pays, and not who
                                charges. The key stays on this phone, to prove what is done with the ticket.
                            </p>

                        </section>
                    ` : nothing}

                    ${session.start !== undefined ? html`
                        <section class="card session-start">

                            <h2><i class="fa-solid fa-file-signature"></i> Start and stop</h2>

                            <div class="kv-list">
                                ${kv('Token',            html`<span class="mono">${session.start.token}</span>`)}
                                ${kv('Reference',        html`<span class="commit">${session.start.authorizationReference}</span>`)}
                                ${kv('Start request',    html`<span class="commit">${session.start.requestDigest}</span>`)}
                                ${kv('Countersigned',    dayAndTime(session.start.countersignedAt))}
                                ${session.start.stopRequestDigest !== undefined ? html`
                                    ${kv('Stop request',  html`<span class="commit">${session.start.stopRequestDigest}</span>`)}
                                    ${kv('Countersigned', dayAndTime(session.start.stopCountersignedAt ?? ''))}
                                ` : nothing}
                            </div>

                            <p class="small muted">
                                Signed on this phone by the ticket's key - this EVSE, this moment, this ticket - and
                                countersigned by the provider, with a token of this start alone: the charge point operator
                                holds both, as proof that the driver asked for exactly this and the provider pays for it.
                                The stop names the start, and is countersigned with the same token.
                            </p>

                        </section>
                    ` : nothing}

                    ${session.check !== undefined ? checkView(session.check, session) : nothing}

                </div>

            `);

        }


        async function stop(): Promise<void> {

            stopping = true;
            failed   = null;

            try
            {
                const stopped = await api.charging.stop(id);

                if (!cancelled)
                    draw(stopped);
            }
            catch (error)
            {
                if (!cancelled)
                    failed = errorMessage(error);
            }
            finally
            {
                stopping = false;
            }

            if (!cancelled)
                await load();

        }


        async function load(): Promise<void> {

            clearTimeout(timer);

            try
            {

                const session = await api.charging.get(id);

                if (cancelled)
                    return;

                draw(session);

                if (isRunning(session))
                    timer = setTimeout(() => void load(), 2_000);

            }
            catch (error)
            {
                if (!cancelled)
                    render(content, html`
                        <p class="small"><a href="${toURL('/charging/history')}"><i class="fa-solid fa-arrow-left"></i> Every session</a></p>
                        <div class="error-box">The charging session could not be loaded: ${errorMessage(error)}</div>
                    `);
            }

        }


        void load();

        return () => { cancelled = true; clearTimeout(timer); };

    }

};
