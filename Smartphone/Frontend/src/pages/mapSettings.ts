import { api, type KeyStatementCheck, type MapSettings, type RoamingNetworkSettings } from '../api/client';
import { auth } from '../auth';
import { dayAndTime } from '../charging/words';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, field, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * Where the map takes its charging stations from: the roaming network whose
 * trust anchor is given here - the text of the QR code its operator prints,
 * its URI or its JSON - or, without one, the demo stations. Setting an anchor
 * trusts none of its identity keys yet: each is trusted here on its own, once
 * the key statement of the operator, asked of the network, was looked at, and
 * a proof holds only by the keys trusted. Beside it one URL, kept in the
 * configuration file and shown - and not asked yet.
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
        let current:       MapSettings | null = null;
        let roaming:       RoamingNetworkSettings | null = null;

        // The key statement of the network's operator, as it was asked last -
        // or why it could not be.
        let keyStatement:  KeyStatementCheck | null = null;
        let keyStatementProblem: string | null = null;

        // The key whose trust waits to be confirmed.
        let confirming:    string | null = null;


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
                                <div class="kv"><span class="k">To agree</span><span class="v">${anchor.minConsensus} of ${anchor.holders.length} holder(s)</span></div>
                                <div class="kv"><span class="k">Trusted here</span><span class="v">keys of ${roaming.trustedHolders} of the ${anchor.minConsensus} holder(s) required</span></div>
                            </div>

                            ${roaming.quorumTrusted ? nothing : html`
                                <div class="notice roaming-network-untrusted">
                                    No station of this network is shown as signed, and none is charged at, until identity keys of
                                    ${anchor.minConsensus} holder(s) are trusted here: setting a trust anchor trusts none of its keys.
                                </div>
                            `}

                            ${keysView(anchor.holders, anchor.roamingNetworkId)}

                            ${keyStatementView()}
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
                                is served and the thumbprints of its operator's identity keys. Setting it trusts none of them: each
                                is trusted here on its own, and every station's proof is checked against those trusted before
                                anything it says is shown - nothing is charged at one whose proof does not hold. A QR code is as
                                trustworthy as the place it was found: one stuck on a charging station may be anybody's.
                                The same anchor set again keeps the keys trusted; another one trusts none. Kept in ${roaming.file}.
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


        /**
         * The identity keys of the trust anchor, per holder: whether each is
         * trusted here, since when and by whom, what the key statement says of
         * it - and, to whoever may change it, the way to trust it, confirmed
         * first, or to trust it no longer.
         */
        function keysView(holders: string[][], network: string): TemplateResult {

            const stated = keyStatement?.keyStatement?.identityKeys ?? [];

            return html`
                <h3>Its operator's identity keys</h3>
                <ul class="trust-keys">
                    ${holders.map((keys, index) => keys.map(id => {

                        const trusted   = roaming?.trustedKeys.find(key => key.id === id);
                        const material  = stated.find(key => key.id === id);

                        return html`
                            <li class="trust-key" data-key-id="${id}">
                                <div>
                                    <span class="small muted">holder ${index + 1}</span>
                                    <span class="commit">${id}</span>
                                    ${trusted
                                          ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> trusted</span>`
                                          : html`<span class="chip warn">not trusted</span>`}
                                </div>
                                ${trusted ? html`
                                    <div class="small muted">Trusted since ${dayAndTime(trusted.acceptedAt)}, by ${trusted.acceptedBy}.</div>
                                ` : nothing}
                                ${material ? html`
                                    <div class="small muted">
                                        The key statement names it${material.holder ? html`, held by ${material.holder}` : nothing}${material.notAfter ? html`, valid until ${dayAndTime(material.notAfter)}` : nothing}.
                                    </div>
                                ` : keyStatement?.keyStatement ? html`
                                    <div class="small form-error">The key statement of the operator does not name it.</div>
                                ` : nothing}
                                ${!mayChange ? nothing : trusted ? html`
                                    <div class="form-actions">
                                        <button type="button" class="btn trust-key-withdraw" @click=${() => setTrust(id, false)}>Trust no longer</button>
                                    </div>
                                ` : confirming === id ? html`
                                    <div class="notice trust-key-confirmation">
                                        Trust the identity key ${id.slice(0, 16)}... of holder ${index + 1} of the roaming network ${network}?
                                        A station is shown as signed, and charged at, where keys trusted here of as many holders as
                                        are required attest its proof. Trust it only where you know it is the operator's - from its
                                        documents, its website, or a QR code you trust - and not from this page alone.
                                        <div class="form-actions">
                                            <button type="button" class="btn primary trust-key-confirm" @click=${() => setTrust(id, true)}>Trust it</button>
                                            <button type="button" class="btn trust-key-cancel" @click=${() => { confirming = null; draw(); }}>Cancel</button>
                                        </div>
                                    </div>
                                ` : html`
                                    <div class="form-actions">
                                        <button type="button" class="btn trust-key-start" @click=${() => { confirming = id; draw(); }}>Trust ...</button>
                                    </div>
                                `}
                            </li>
                        `;

                    }))}
                </ul>
                <div class="form-actions">
                    <span id="roaming-network-trust-note"  class="form-notice" role="status"></span>
                    <span id="roaming-network-trust-error" class="form-error"  role="alert"></span>
                </div>
            `;

        }


        /**
         * The key statement of the operator, as the network served it: by whom
         * it is signed, whether validly, until when it is valid, and whether the
         * keys trusted here are a quorum of it.
         */
        function keyStatementView(): TemplateResult {

            if (keyStatementProblem !== null)
                return html`<div class="error-box key-statement">The key statement of the operator could not be asked: ${keyStatementProblem}</div>`;

            if (keyStatement === null)
                return html`<p class="small muted key-statement">Asking the network for the key statement of its operator ...</p>`;

            const statement   = keyStatement.keyStatement;
            const evaluation  = keyStatement.evaluation;

            if (statement === null || evaluation === null)
                return html`<div class="error-box key-statement">No key statement of the operator: ${keyStatement.error ?? 'none was given'}</div>`;

            const yesNo = (value: boolean): TemplateResult =>
                value ? html`<span class="chip ok"><i class="fa-solid fa-check"></i> yes</span>`
                      : html`<span class="chip bad"><i class="fa-solid fa-xmark"></i> no</span>`;

            return html`
                <h3>The key statement of its operator</h3>
                <div class="kv-list key-statement">
                    <div class="kv"><span class="k">Operator</span><span class="v">${statement.operatorId}</span></div>
                    <div class="kv"><span class="k">Served by</span><span class="v">${keyStatement.from ?? '-'}</span></div>
                    <div class="kv"><span class="k">Signed</span><span class="v">${dayAndTime(statement.signedAt)}, ${evaluation.signatures} signature(s)</span></div>
                    <div class="kv"><span class="k">Valid until</span><span class="v">${dayAndTime(statement.validUntil)}</span></div>
                    <div class="kv"><span class="k">Signed validly</span><span class="v">${yesNo(evaluation.signaturesValid)}</span></div>
                    <div class="kv"><span class="k">Valid now</span><span class="v">${yesNo(evaluation.withinValidity)}</span></div>
                    <div class="kv"><span class="k">By keys trusted here</span><span class="v">${yesNo(evaluation.quorumReached)} ${evaluation.trustedHolders} of the ${evaluation.requiredConsensus} holder(s) required</span></div>
                </div>
                ${evaluation.canTrustKeys ? nothing : html`
                    <div class="error-box">
                        This key statement is not signed validly, or not valid now: what it names is no reason to trust a key.
                        ${evaluation.error ?? ''}
                    </div>
                `}
                <p class="small muted">
                    Asked ${dayAndTime(keyStatement.checkedAt)}. A valid signature says only that the keys it names signed it -
                    whether they are the operator's is what is decided here.
                </p>
            `;

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
                current       = await api.map.settings();
                keyStatement  = null;
                confirming    = null;

                if (cancelled)
                    return;

                draw();
                form.reset();

                must<HTMLElement>(content, '#roaming-network-note').textContent =
                    roaming.trustAnchor === null
                        ? 'Saved: the map shows its demo stations.'
                        : roaming.quorumTrusted
                              ? `Saved: the map shows the stations of ${roaming.trustAnchor.name ?? roaming.trustAnchor.roamingNetworkId}.`
                              : `Saved: the map shows the stations of ${roaming.trustAnchor.name ?? roaming.trustAnchor.roamingNetworkId} - and none as signed, before its keys are trusted.`;

                void loadKeyStatement();

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#roaming-network-error').textContent = errorMessage(problem);
            }

        }


        async function setTrust(keyId: string, trusted: boolean): Promise<void> {

            const note = must<HTMLElement>(content, '#roaming-network-trust-note');

            note.textContent = '';
            must<HTMLElement>(content, '#roaming-network-trust-error').textContent = '';

            try
            {

                roaming = await whileSaving(content, note, () => trusted ? api.roamingNetwork.trustKey(keyId) : api.roamingNetwork.distrustKey(keyId));

                if (cancelled)
                    return;

                confirming = null;
                draw();

                must<HTMLElement>(content, '#roaming-network-trust-note').textContent =
                    `${trusted ? 'Trusted' : 'No longer trusted'}: keys of ${roaming.trustedHolders} holder(s) are trusted here` +
                    (roaming.quorumTrusted ? '.' : ', fewer than required.');

                // Whether those trusted are a quorum of the key statement has
                // just changed.
                void loadKeyStatement();

            }
            catch (problem)
            {
                must<HTMLElement>(content, '#roaming-network-trust-error').textContent = errorMessage(problem);
            }

        }


        /**
         * The key statement of the operator, asked of the network anew - only
         * where there is a trust anchor to ask by.
         */
        async function loadKeyStatement(): Promise<void> {

            if (roaming === null || roaming.trustAnchor === null)
                return;

            try
            {
                const asked = await api.roamingNetwork.keyStatement();

                if (!cancelled) {
                    keyStatement         = asked;
                    keyStatementProblem  = null;
                    draw();
                }
            }
            catch (problem)
            {
                if (!cancelled) {
                    keyStatementProblem = errorMessage(problem);
                    draw();
                }
            }

        }


        async function load(): Promise<void> {

            try
            {
                const [ loaded, network ] = await Promise.all([ api.map.settings(), api.roamingNetwork.settings() ]);

                if (!cancelled) {
                    current              = loaded;
                    roaming              = network;
                    keyStatement         = null;
                    keyStatementProblem  = null;
                    draw();
                    void loadKeyStatement();
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
