import { api, type ProviderAccount, type ProviderSettings } from '../api/client';
import { auth } from '../auth';
import { dayAndTime } from '../charging/words';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, field, whileSaving } from '@node/ui';
import { typedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * The account of this phone at an e-mobility provider: made by the phone -
 * with a password it makes up and keeps, which nobody has to type - or one the
 * driver has already, taken with its password; and either way an account key
 * made on the phone, which the provider certifies from a signing request and
 * never sees. With it, every "Charge now!" asks the provider for a charging
 * ticket of its own, signed so that a charge point operator learns who pays
 * and not who charges.
 */
export const providerPage: Page = {

    title: 'E-mobility provider',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/provider',
            title:     'E-mobility provider',
            subtitle:  'The account this phone charges with, and its keys.',
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayChange = auth.can('provider', 'edit');

        let cancelled  = false;
        let current:   ProviderSettings | null = null;
        let said:      string | null = null;


        function kv(Key: string, Value: TemplateResult | string): TemplateResult {
            return html`<div class="kv"><span class="k">${Key}</span><span class="v">${Value}</span></div>`;
        }


        function draw(): void {

            if (current === null)
                return;

            const settings = current;

            render(content, html`

                ${mayChange ? nothing : html`
                    <div class="notice">${mayButNot('look at the account at the e-mobility provider', 'make, take or forget one')}</div>
                `}

                ${said !== null ? html`<div class="notice" id="provider-note" role="status">${said}</div>` : nothing}

                <div class="cards">
                    ${settings.account !== null ? accountView(settings.account, settings.ticketValidity) : signUpView()}
                </div>

            `);

        }


        function accountView(account: ProviderAccount, validity: number): TemplateResult {

            return html`

                <section class="card provider-account">

                    <h2><i class="fa-solid fa-id-card"></i> ${account.party}</h2>

                    <div class="kv-list">
                        ${kv('Provider',      account.url)}
                        ${kv('Account',       account.username)}
                        ${account.email.length > 0 ? kv('E-mail', account.email) : nothing}
                        ${kv('Password',      account.madeUp ? 'made up by this phone, and kept by it alone' : 'the driver\'s, kept by this phone')}
                        ${kv('Since',         dayAndTime(account.since))}
                    </div>

                    ${mayChange ? html`
                        <div class="form-actions">
                            <button type="button" id="provider-forget" class="btn danger" @click=${() => void forget()}>Forget this account</button>
                            <span id="provider-error" class="form-error" role="alert"></span>
                        </div>
                    ` : nothing}

                </section>

                <section class="card">

                    <h2><i class="fa-solid fa-key"></i> Account key</h2>

                    <div class="kv-list">
                        ${kv('Certificate',   html`<span class="commit">${account.accountKey.id}</span>`)}
                        ${kv('Subject',       account.accountKey.subject)}
                        ${kv('Certified by',  account.accountKey.issuer)}
                        ${kv('Valid until',   dayAndTime(account.accountKey.notAfter))}
                    </div>

                    <p class="small muted">
                        Made on this phone; the provider got a signing request and certified the key it carries.
                        It signs every ticket request, and the provider takes its signature off before the
                        ticket goes anywhere.
                    </p>

                </section>

                <section class="card">

                    <h2><i class="fa-solid fa-ticket"></i> Charging tickets</h2>

                    <div class="kv-list">
                        ${kv('Issuer',        account.ticketIssuer.subject)}
                        ${kv('Fingerprint',   html`<span class="commit">${account.ticketIssuer.fingerprint}</span>`)}
                        ${kv('Valid until',   dayAndTime(account.ticketIssuer.notAfter))}
                    </div>

                    <p class="small muted">
                        Every "Charge now!" asks for a ticket of its own: a key made for that start alone, good for
                        ${validity} minutes, signed by the provider's ticket issuer. A charge point operator sees that key
                        and the provider, never this account - and no two sessions with the same key. The start and
                        the stop are requests signed by that key, which the provider countersigns.
                    </p>

                </section>

            `;

        }


        function signUpView(): TemplateResult {

            return html`

                <section class="card">

                    <h2><i class="fa-solid fa-user-plus"></i> A new account</h2>

                    <form id="provider-signup-form" class="form-stack" @submit=${signUp}>

                        <label>Provider
                            <input type="url" name="url" placeholder="https://emsp.example.org/" ?disabled=${!mayChange} />
                        </label>

                        <label>Account
                            <input type="text" name="username" autocomplete="off" ?disabled=${!mayChange} />
                        </label>

                        <label>E-mail address
                            <input type="email" name="email" autocomplete="off" ?disabled=${!mayChange} />
                        </label>

                        <div class="form-actions">
                            <button type="submit" class="btn primary" ?disabled=${!mayChange}>Sign up</button>
                            <span id="provider-signup-error" class="form-error" role="alert"></span>
                        </div>

                        <span class="hint">
                            This phone makes the account, with a password it makes up and keeps - nobody has to type
                            it - and a key of its own, which the provider certifies and never sees.
                        </span>

                    </form>

                </section>

                <section class="card">

                    <h2><i class="fa-solid fa-right-to-bracket"></i> An account the driver has</h2>

                    <form id="provider-signin-form" class="form-stack" @submit=${signIn}>

                        <label>Provider
                            <input type="url" name="url" placeholder="https://emsp.example.org/" ?disabled=${!mayChange} />
                        </label>

                        <label>Account
                            <input type="text" name="username" autocomplete="username" ?disabled=${!mayChange} />
                        </label>

                        <label>Password
                            <input type="password" name="password" autocomplete="current-password" ?disabled=${!mayChange} />
                        </label>

                        <div class="form-actions">
                            <button type="submit" class="btn primary" ?disabled=${!mayChange}>Sign in</button>
                            <span id="provider-signin-error" class="form-error" role="alert"></span>
                        </div>

                        <span class="hint">
                            Its password is kept by this phone from now on, to sign in with; the provider certifies a
                            key of this phone for it, beside whatever keys the account has already.
                        </span>

                    </form>

                </section>

            `;

        }


        async function signUp(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form  = event.currentTarget as HTMLFormElement;
            const asked = { url: field(form, 'url'), username: field(form, 'username'), email: field(form, 'email') };

            await save('#provider-signup-error', () => api.provider.signUp(asked));

        }


        async function signIn(event: SubmitEvent): Promise<void> {

            event.preventDefault();

            const form  = event.currentTarget as HTMLFormElement;
            const asked = { url: field(form, 'url'), username: field(form, 'username'), password: field(form, 'password', false) };

            await save('#provider-signin-error', () => api.provider.signIn(asked));

        }


        async function forget(): Promise<void> {
            await save('#provider-error', () => api.provider.forget());
        }


        /** Ask the smartphone, and show the account as it is now - or say why not, beside the form asked from. */
        async function save(errorAt: string, ask: () => Promise<ProviderSettings>): Promise<void> {

            const error = must<HTMLElement>(content, errorAt);

            error.textContent = '';

            try
            {

                const answer = await whileSaving(content, null, ask);

                if (cancelled)
                    return;

                current  = answer;
                said     = answer.message ?? null;

                draw();

            }
            catch (problem)
            {
                if (!cancelled && content.querySelector(errorAt) !== null)
                    must<HTMLElement>(content, errorAt).textContent = errorMessage(problem);
            }

        }


        async function load(): Promise<void> {

            try
            {
                const loaded = await api.provider.get();

                if (!cancelled) {
                    current = loaded;
                    said    = null;
                    draw();
                }
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`
                        <div class="error-box">The account at the e-mobility provider could not be loaded: ${errorMessage(problem)}</div>
                    `);
            }

        }

        const release = unsaved.heldBy(() => typedSinceDrawn(content.querySelector('#provider-signup-form')) ||
                                             typedSinceDrawn(content.querySelector('#provider-signin-form')));

        void load();

        return () => { cancelled = true; release(); };

    }

};
