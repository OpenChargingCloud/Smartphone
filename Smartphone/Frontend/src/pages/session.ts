import { api, type ChargingSession } from '../api/client';
import { auth } from '../auth';
import { dayAndTime, duration, isRunning, money, reportedInWords, sessionStatusClass, sessionStatusWord, withUnit } from '../charging/words';
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
 * every two seconds and Stop. A session the e-mobility provider reports has
 * no power, no target and no time it is full at: OCPI tells none of them.
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

                        ${running && mayStop ? html`
                            <div class="form-actions">
                                <button type="button" class="btn danger" ?disabled=${stopping} @click=${() => void stop()}>
                                    <i class="fa-solid fa-stop"></i> ${stopping ? 'Stopping ...' : 'Stop charging'}
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

                        ${!session.simulated && session.provider !== undefined ? html`
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
