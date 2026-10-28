import { api, type ChargingSession, type ChargingSessions } from '../api/client';
import { dayAndTime, duration, money, sessionStatusClass, sessionStatusWord, withUnit } from '../charging/words';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, nothing, render, repeat } from '@node/view';

/**
 * Every charging session of this phone, running and past, the newest first:
 * one row each, and the row opens the session with everything there is to
 * know about it.
 *
 * Asked again every five seconds while one of them still charges, so that its
 * row counts on; a list of sessions that are all over is asked once.
 */
export const historyPage: Page = {

    title: 'Charging history',

    render({ root, navigate }) {

        const content = shell(root, {
            active:    '/charging/history',
            title:     'Charging history',
            subtitle:  'Every charging session of this phone, running and past.',
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        let cancelled  = false;
        let timer:     ReturnType<typeof setTimeout> | undefined;

        const open = (session: ChargingSession) => navigate(`/charging/sessions/${encodeURIComponent(session.id)}`);


        function draw(all: ChargingSessions): void {

            render(content, html`

                ${all.sessions.length === 0 ? html`
                    <section class="card">
                        <h2><i class="fa-solid fa-clock-rotate-left"></i> No sessions yet</h2>
                        <p>This phone has not charged anywhere yet. Find a station on the <a href="${toURL('/map')}">map</a>.</p>
                    </section>
                ` : html`

                    <section class="card wide">

                        <h2>
                            <i class="fa-solid fa-clock-rotate-left"></i> ${all.total} session(s)
                            ${all.active > 0 ? html`<span class="muted small">- ${all.active} charging now</span>` : nothing}
                        </h2>

                        <div class="table-scroll"><table class="records session-table">
                            <thead>
                                <tr>
                                    <th>Started</th>
                                    <th>Station</th>
                                    <th>Charge point</th>
                                    <th>Status</th>
                                    <th class="number">Energy</th>
                                    <th class="number">Duration</th>
                                    <th class="number">Cost</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${repeat(all.sessions, session => session.id, session => html`
                                    <tr class="clickable" @click=${() => open(session)}>
                                        <td><a href="${toURL(`/charging/sessions/${encodeURIComponent(session.id)}`)}"
                                               @click=${(event: Event) => event.stopPropagation()}>${dayAndTime(session.startedAt)}</a></td>
                                        <td>${session.location.name}<div class="small muted">${session.location.city}</div></td>
                                        <td class="mono small">${session.evse.evseId}</td>
                                        <td><span class="session-status ${sessionStatusClass(session.status)}">${sessionStatusWord(session.status)}</span></td>
                                        <td class="number">${withUnit(session.energyKWh, 'kWh', 2)}</td>
                                        <td class="number">${duration(session.durationSeconds)}</td>
                                        <td class="number">${money(session.cost.amount, session.cost.currency)}</td>
                                    </tr>
                                `)}
                            </tbody>
                        </table></div>

                        ${all.simulated ? html`
                            <p class="small muted">
                                ${all.sessions.some(session => session.start !== undefined)
                                      ? 'Simulated figures: no meter values reach this phone yet. A session not started through an e-mobility provider charged nothing for real.'
                                      : 'Simulated sessions: no e-mobility provider was asked, and nothing was charged for real.'}
                            </p>
                        ` : nothing}

                    </section>

                `}

            `);

        }


        async function load(): Promise<void> {

            clearTimeout(timer);

            try
            {

                const all = await api.charging.list();

                if (cancelled)
                    return;

                draw(all);

                if (all.active > 0)
                    timer = setTimeout(() => void load(), 5_000);

            }
            catch (error)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The charging sessions could not be loaded: ${errorMessage(error)}</div>`);
            }

        }


        void load();

        return () => { cancelled = true; clearTimeout(timer); };

    }

};
