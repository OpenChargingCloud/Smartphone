import { api, type ChargingSession } from '../api/client';
import { auth } from '../auth';
import { dayAndTime, duration, money, sessionStatusClass, sessionStatusWord, withUnit } from '../charging/words';
import { standardWord } from '../map/stations';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * One charging session, with everything there is to know about it: where,
 * at which charge point, from when to when, what it charged and what it
 * cost - and, while it still charges, its figures counting on every two
 * seconds and Stop.
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

            const charging = session.status === 'charging';

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
                            ${kv('Power',      charging ? withUnit(session.powerKW, 'kW') : 'none - it is over')}
                            ${kv('Duration',   duration(session.durationSeconds))}
                            ${kv('Cost',       html`${money(session.cost.amount, session.cost.currency)}
                                                    <span class="muted">(${money(session.cost.pricePerKWh, session.cost.currency)} per kWh)</span>`)}
                            ${kv('Progress',   html`${withUnit(session.progressPercent, '%')} of ${withUnit(session.targetEnergyKWh, 'kWh')}`)}
                        </div>

                        ${charging ? html`
                            <progress class="session-progress" max="100" .value=${session.progressPercent}
                                      aria-label="How far it has charged"></progress>
                        ` : nothing}

                        ${charging && mayStop ? html`
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
                            ${kv(charging ? 'Full at' : 'Ended',
                                 charging ? dayAndTime(session.fullAt) : dayAndTime(session.endedAt ?? session.fullAt))}
                            ${session.stoppedBy !== undefined ? kv('Stopped by', session.stoppedBy) : nothing}
                            ${kv('Session',     html`<span class="mono">${session.id}</span>`)}
                        </div>

                        ${session.simulated ? html`
                            <p class="small muted">Simulated: no e-mobility provider was asked, and nothing was charged for real.</p>
                        ` : nothing}

                    </section>

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

                if (session.status === 'charging')
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
