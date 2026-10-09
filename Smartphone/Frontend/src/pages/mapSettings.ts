import { api, type MapSettings } from '../api/client';
import { auth } from '../auth';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, field, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render } from '@node/view';

/**
 * Where the map takes its charging stations from: one URL, kept in the
 * configuration file and shown here - and not asked yet. Until it is, the map
 * shows its demo stations whatever this says; empty, the default, says that
 * there is no source.
 */
export const mapSettingsPage: Page = {

    title: 'Map',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/map',
            title:     'Map',
            subtitle:  'Where the map takes its charging stations from.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayChange = auth.can('map', 'edit');

        let cancelled = false;
        let current:  MapSettings | null = null;


        function draw(): void {

            if (current === null)
                return;

            const settings = current;

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at where the map takes its stations from', 'change it')}</div>
                `}

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-map"></i> Charging station data</h2>

                        <form id="map-settings-form" class="form-stack" @submit=${save}>

                            <label>URL
                                <input type="url" name="stationsURL" value="${settings.stationsURL}"
                                       maxlength="${settings.maxURLLength}"
                                       placeholder="https://emsp.example.org/api/v1/locations"
                                       ?disabled=${!mayChange} />
                            </label>

                            <div class="form-actions">
                                <button type="submit" class="btn primary" ?disabled=${!mayChange}>Save</button>
                                <span id="map-settings-note"  class="form-notice" role="status"></span>
                                <span id="map-settings-error" class="form-error"  role="alert"></span>
                            </div>

                            <span class="hint">
                                Kept in ${settings.file}, and not asked yet: the map shows its
                                ${settings.source === 'demo' ? 'demo stations' : 'stations'} whatever this says.
                                Empty means no source at all. An absolute http or https URL, of at most
                                ${settings.maxURLLength} characters.
                            </span>

                        </form>

                    </section>

                </div>

            `);

        }


        async function save(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form         = event.currentTarget as HTMLFormElement;
            const note         = must<HTMLElement>(content, '#map-settings-note');

            // Read before whileSaving() switches the form off: a field that
            // is off is not in its FormData.
            const stationsURL  = field(form, 'stationsURL');

            note.textContent = '';
            must<HTMLElement>(content, '#map-settings-error').textContent = '';

            try
            {

                current = await whileSaving(content, note, () => api.map.saveSettings({ stationsURL }));

                if (cancelled)
                    return;

                draw();

                // A draw leaves a form as it is typed into; this one was
                // saved, so it goes back to what it says now - the answer.
                form.reset();

                note.textContent = current.stationsURL.length > 0 ? 'Saved.' : 'Saved: the map has no data source.';

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#map-settings-error').textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            try
            {
                const loaded = await api.map.settings();

                if (!cancelled) {
                    current = loaded;
                    draw();
                }
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`
                        <div class="error-box">Where the map takes its stations from could not be loaded: ${errorMessage(problem)}</div>
                    `);
            }

        }

        /**
         * Loaded anew - Reload - is what the smartphone has, the form too,
         * which a draw on its own would leave as typed.
         */
        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#map-settings-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
