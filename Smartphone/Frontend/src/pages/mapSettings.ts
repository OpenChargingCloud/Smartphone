import { api, type MapSettings, type RoamingNetworkSettings } from '../api/client';
import { auth } from '../auth';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, field, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render } from '@node/view';

/**
 * Where the map takes its charging stations from: the roaming network whose
 * trust anchor is given here - the text of the QR code its operator prints,
 * its URI or its JSON - or, without one, the demo stations. Beside it one URL,
 * kept in the configuration file and shown - and not asked yet.
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
        let roaming:  RoamingNetworkSettings | null = null;


        function draw(): void {

            if (current === null || roaming === null)
                return;

            const settings  = current;
            const anchor    = roaming.trustAnchor;

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at where the map takes its stations from', 'change it')}</div>
                `}

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-diagram-project"></i> Roaming network</h2>

                        ${anchor !== null ? html`
                            <div class="kv-list roaming-network-anchor">
                                <div class="kv"><span class="k">Roaming network</span><span class="v">${anchor.name ? `${anchor.name} (${anchor.roamingNetworkId})` : anchor.roamingNetworkId}</span></div>
                                <div class="kv"><span class="k">Served at</span><span class="v">${anchor.urls.map(url => html`<div>${url}</div>`)}</span></div>
                                <div class="kv"><span class="k">Its operator's keys</span><span class="v">
                                    ${anchor.holders.map((keys, index) => html`<div>holder ${index + 1}: ${keys.map(key => html`<span class="commit">${key}</span> `)}</div>`)}
                                </span></div>
                                <div class="kv"><span class="k">To agree</span><span class="v">${anchor.minConsensus} of ${anchor.holders.length} holder(s)</span></div>
                            </div>
                        ` : html`
                            <p class="small">None: the map shows its demo stations.</p>
                        `}

                        <form id="roaming-network-form" class="form-stack" @submit=${saveTrustAnchor}>

                            <label>Trust anchor
                                <textarea name="trustAnchor" rows="4" maxlength="${roaming.maxTextLength}"
                                          placeholder="WWCP/TA-2:... or https://hub.example/poi/RNs/QA?keys=..."
                                          ?disabled=${!mayChange}></textarea>
                            </label>

                            <div class="form-actions">
                                <button type="submit" class="btn primary" ?disabled=${!mayChange}>Save</button>
                                ${anchor !== null ? html`
                                    <button type="button" id="roaming-network-remove" class="btn" ?disabled=${!mayChange} @click=${removeTrustAnchor}>Remove</button>
                                ` : nothing}
                                <span id="roaming-network-note"  class="form-notice" role="status"></span>
                                <span id="roaming-network-error" class="form-error"  role="alert"></span>
                            </div>

                            <span class="hint">
                                What the operator of a roaming network prints as a QR code, on its website or in its documents:
                                the text of the code, WWCP/TA-2: and what follows, its URI or its JSON. It names where the network
                                is served and the thumbprints of its operator's identity keys - the only thing trusted: every
                                station's proof is checked against them before anything it says is shown, and nothing is
                                charged at one whose proof does not hold. A QR code is as trustworthy as the place it was found:
                                one stuck on a charging station may be anybody's. Kept in ${roaming.file}.
                            </span>

                        </form>

                    </section>

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
                                Kept in ${settings.file}, and not asked yet: the map shows
                                ${settings.source === 'demo' ? 'its demo stations' : 'the stations of its roaming network'} whatever this says.
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


        async function saveTrustAnchor(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form         = event.currentTarget as HTMLFormElement;
            const trustAnchor  = field(form, 'trustAnchor');

            await saveRoamingNetwork(form, trustAnchor);

        }


        async function removeTrustAnchor(): Promise<void> {
            await saveRoamingNetwork(must<HTMLFormElement>(content, '#roaming-network-form'), '');
        }


        async function saveRoamingNetwork(form: HTMLFormElement, trustAnchor: string): Promise<void> {

            const note = must<HTMLElement>(content, '#roaming-network-note');

            note.textContent = '';
            must<HTMLElement>(content, '#roaming-network-error').textContent = '';

            try
            {

                roaming = await whileSaving(content, note, () => api.roamingNetwork.saveSettings({ trustAnchor }));

                if (cancelled)
                    return;

                // The map's own settings say where it takes its stations from,
                // which has just changed.
                current = await api.map.settings();

                if (cancelled)
                    return;

                draw();
                form.reset();

                must<HTMLElement>(content, '#roaming-network-note').textContent =
                    roaming.trustAnchor !== null
                        ? `Saved: the map shows the stations of ${roaming.trustAnchor.name ?? roaming.trustAnchor.roamingNetworkId}.`
                        : 'Saved: the map shows its demo stations.';

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#roaming-network-error').textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            try
            {
                const [ loaded, network ] = await Promise.all([ api.map.settings(), api.roamingNetwork.settings() ]);

                if (!cancelled) {
                    current = loaded;
                    roaming = network;
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

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#map-settings-form')) ||
                                             typedSinceDrawn(content.querySelector('#roaming-network-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
