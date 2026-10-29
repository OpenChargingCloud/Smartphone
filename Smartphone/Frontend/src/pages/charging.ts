import { api, type ChargingSession, type ChargingSessions } from '../api/client';
import { auth } from '../auth';
import { clock, duration, money, reportedInWords, sessionStatusClass, sessionStatusWord, withUnit } from '../charging/words';
import { standardWord } from '../map/stations';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, nothing, render, repeat, type TemplateResult } from '@node/view';

/**
 * What this phone is charging right now - one card per session, as several
 * may run at once at different stations - with what each has charged, how
 * fast, for how long, what it costs so far, and Stop. How fast, and how full,
 * only where it is simulated: of a session the e-mobility provider reports,
 * OCPI tells neither.
 *
 * Asked again every two seconds while something charges, and every ten while
 * nothing does, so that a session started from the map or over SSH shows up
 * without a Reload. The session "Charge now!" just started is marked: the
 * map sends its id along.
 */
export const chargingPage: Page = {

    title: 'Charging',

    render({ root, url }) {

        const content = shell(root, {
            active:    '/charging',
            title:     'Charging',
            subtitle:  'What this phone is charging right now.',
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayStop   = auth.can('charging', 'run');
        const started   = url.searchParams.get('session');

        let cancelled   = false;
        let current:    ChargingSessions | null = null;
        let problem:    string | null = null;
        let timer:      ReturnType<typeof setTimeout> | undefined;

        const stopping  = new Set<string>();
        const failed    = new Map<string, string>();


        function draw(): void {

            if (current === null && problem === null)
                return;

            const active = current?.sessions ?? [];

            // The one just started, if it is over already - a stop on another
            // tab, say - is still shown, so that "Charge now!" never leads to
            // a page that says nothing about it.
            const startedAndOver = started !== null && !active.some(session => session.id === started);

            render(content, html`

                ${problem !== null ? html`<div class="error-box">The charging sessions could not be loaded: ${problem}</div>` : nothing}

                ${current?.simulated ? html`
                    <div class="notice small">
                        ${active.some(session => session.start !== undefined)
                              ? 'Simulated figures, where the e-mobility provider reports nothing of a session. A session not started through an e-mobility provider charges nothing for real.'
                              : 'Simulated: no e-mobility provider is asked yet, and nothing is charged for real.'}
                    </div>
                ` : nothing}

                ${!mayStop ? html`<div class="notice">${mayButNot('look at the charging sessions', 'start or stop one')}</div>` : nothing}

                ${active.length === 0 ? html`
                    <section class="card">
                        <h2><i class="fa-solid fa-plug"></i> Nothing is charging</h2>
                        <p>
                            ${startedAndOver
                                  ? html`The session just started is over already - it is in the <a href="${toURL(`/charging/sessions/${encodeURIComponent(started ?? "")}`)}">history</a>.`
                                  : html`Find a charging station on the <a href="${toURL('/map')}">map</a> and press "Charge now!" there.`}
                        </p>
                    </section>
                ` : html`
                    <div class="cards">
                        ${repeat(active, session => session.id, session => sessionCard(session))}
                    </div>
                `}

                <p class="small">
                    <a href="${toURL('/charging/history')}"><i class="fa-solid fa-clock-rotate-left"></i>
                        Every session of this phone, running and past</a>
                    ${current !== null ? html`<span class="muted"> - ${current.total} in all.</span>` : nothing}
                </p>

            `);

        }


        function sessionCard(session: ChargingSession): TemplateResult {

            const busy = stopping.has(session.id);

            return html`

                <section class="card session-card ${session.id === started ? 'just-started' : ''}">

                    <h2>
                        <i class="fa-solid fa-bolt"></i>
                        <a href="${toURL(`/charging/sessions/${encodeURIComponent(session.id)}`)}">${session.location.name}</a>
                        <span class="session-status ${sessionStatusClass(session.status)}">${sessionStatusWord(session.status)}</span>
                    </h2>

                    <div class="small muted">
                        ${session.evse.evseId} - ${standardWord(session.evse.standard)},
                        ${session.location.address}, ${session.location.city}
                    </div>

                    <div class="session-figures">
                        <div><span class="figure">${withUnit(session.energyKWh, 'kWh', 2)}</span><span class="small muted">charged</span></div>
                        ${session.powerKW !== undefined ? html`
                            <div><span class="figure">${withUnit(session.powerKW, 'kW')}</span><span class="small muted">now</span></div>
                        ` : nothing}
                        <div><span class="figure">${duration(session.durationSeconds)}</span><span class="small muted">since ${clock(session.chargingSince ?? session.startedAt)}</span></div>
                        <div><span class="figure">${money(session.cost.amount, session.cost.currency)}</span><span class="small muted">so far</span></div>
                    </div>

                    ${session.progressPercent !== undefined && session.targetEnergyKWh !== undefined && session.fullAt !== undefined ? html`

                        <progress class="session-progress" max="100" .value=${session.progressPercent}
                                  aria-label="How far it has charged"></progress>

                        <div class="small muted">
                            ${withUnit(session.progressPercent, '%')} of ${withUnit(session.targetEnergyKWh, 'kWh')} -
                            full at ${clock(session.fullAt)}, if nobody stops it first.
                        </div>

                    ` : html`
                        <div class="small muted session-reported">${reportedInWords(session)}</div>
                    `}

                    ${mayStop ? html`
                        <div class="form-actions">
                            <button type="button" class="btn danger" ?disabled=${busy} @click=${() => void stop(session)}>
                                <i class="fa-solid fa-stop"></i> ${busy ? 'Stopping ...' : 'Stop charging'}
                            </button>
                            ${failed.has(session.id) ? html`<span class="form-error" role="alert">${failed.get(session.id)}</span>` : nothing}
                        </div>
                    ` : nothing}

                </section>

            `;

        }


        async function stop(session: ChargingSession): Promise<void> {

            stopping.add(session.id);
            failed.delete(session.id);
            draw();

            try
            {
                await api.charging.stop(session.id);
            }
            catch (error)
            {
                if (!cancelled)
                    failed.set(session.id, errorMessage(error));
            }
            finally
            {
                stopping.delete(session.id);
            }

            if (!cancelled)
                await load();

        }


        async function load(): Promise<void> {

            clearTimeout(timer);

            try
            {

                const loaded = await api.charging.list(true);

                if (cancelled)
                    return;

                current = loaded;
                problem = null;

            }
            catch (error)
            {

                if (cancelled)
                    return;

                problem = errorMessage(error);

            }

            draw();

            if (!cancelled)
                timer = setTimeout(() => void load(), (current?.active ?? 0) > 0 ? 2_000 : 10_000);

        }


        void load();

        return () => { cancelled = true; clearTimeout(timer); };

    }

};
