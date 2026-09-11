import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';
import { i18n, setLanguage } from './index';
import enumsEn from './locales/en/enums.json';
import enumsEs from './locales/es/enums.json';
import { IdentityProvider } from '../features/identity/context/IdentityProvider';
import { IdentityContextPage } from '../features/identity/context/IdentityContextPage';
import { RolesPage } from '../features/identity/roles/RolesPage';
import { MembersPage } from '../features/identity/members/MembersPage';
import { InviteMemberPage } from '../features/identity/invitations/InviteMemberPage';
import { SessionsPage } from '../features/identity/sessions/SessionsPage';
import { PlatformPanel } from '../features/platform/PlatformPanel';
import { PlatformIdentitiesPage } from '../features/platform/identities/PlatformIdentitiesPage';
import { PlatformRetentionPage } from '../features/platform/retention/PlatformRetentionPage';
import { server } from '../test/server';
import { antiforgery, contextIs, signedInContext } from '../test/identityServer';

const instant = '2026-09-08T23:15:00Z';
const dateIn = (language) => new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(instant));
const role = (roleId, isSystem, name = 'Owner') => ({ roleId, name, isSystem, isRetired: false, permissions: ['members.read'], version: '1' });
const roles = [role('system', true), role('custom', false), role('admin', true, 'Administrator')];
const pageOf = (items) => ({ items, nextCursor: null });
const get = (path, body) => http.get(path, () => HttpResponse.json(body));
const expectedEnglishEnumLabels = {
  tenantStatus: { PendingConfirmation: 'Pending confirmation' },
  membershipStatus: { PendingConfirmation: 'Pending confirmation' },
  tenantSuspensionReason: {
    PolicyViolation: 'Policy violation',
    SecurityIncident: 'Security incident',
    BillingHold: 'Billing hold',
    OperatorRequest: 'Operator request',
  },
  identitySuspensionReason: {
    PolicyViolation: 'Policy violation',
    SecurityIncident: 'Security incident',
    BillingHold: 'Billing hold',
    OperatorRequest: 'Operator request',
  },
  retentionCategory: {
    PersonalProfileNames: 'Personal profile names',
    PersonalIdentityDocument: 'Personal identity document',
    SessionRecords: 'Session records',
    AuditEvents: 'Audit events',
    OutboxMessages: 'Outbox messages',
    OutboxSecrets: 'Outbox secrets',
    DeliveryEvidence: 'Delivery evidence',
    PlatformMfaMaterial: 'Platform second factor material',
  },
  retentionTrigger: {
    RecordCreation: 'Record creation',
    LastActivity: 'Last activity',
    AccountClosure: 'Account closure',
  },
  accountStatus: {
    PendingConfirmation: 'Pending confirmation',
    AdministrativelySuspended: 'Administratively suspended',
    SelfDeactivated: 'Self-deactivated',
  },
};
const platformContext = () => signedInContext({
  activeTenant: { id: 'platform-1', type: 'Platform', name: 'platform' },
  permissions: Object.keys(enumsEn.permissions),
});
const show = (Component, context = signedInContext()) => {
  server.use(antiforgery(), contextIs(context),
    get('/api/identity/credentials', { hasPassword: true, passwordUpdatedAt: null }),
    get('/api/identity/external', []));
  return render(<MemoryRouter><IdentityProvider><Component /></IdentityProvider></MemoryRouter>);
};

beforeEach(async () => { await act(() => setLanguage('es')); });

describe('Spanish system presentation', () => {
  it('describes a personal context without calling it an organization', async () => {
    const { container } = show(IdentityContextPage, signedInContext({
      activeTenant: { id: 'personal-1', type: 'Personal', name: 'Ana personal' }, permissions: [],
    }));
    expect(await screen.findByText('Ana personal')).toBeVisible();
    expect(screen.getByText('Espacio de trabajo activo')).toBeVisible();
    expect(screen.getByText('ninguno en este espacio de trabajo')).toBeVisible();
    expect(container).not.toHaveTextContent(/organización/i);
    for (const [key, text] of Object.entries({
      'common:navigation.changeOrganization': 'Cambiar de espacio de trabajo',
      'common:navigation.noOrganizationSelected': 'Ningún espacio de trabajo seleccionado',
      'identity:tenants.title': 'Elegir un espacio de trabajo',
      'identity:tenants.empty': 'Todavía no tiene acceso a ningún espacio de trabajo.',
      'identity:context.noneSelected': 'ninguno seleccionado',
    })) expect(i18n.t(key)).toBe(text);
    for (const [key, text] of Object.entries({
      'common:navigation.changeOrganization': 'Change workspace',
      'common:navigation.noOrganizationSelected': 'No workspace selected',
      'identity:context.activeOrganization': 'Active workspace',
      'identity:context.noneInThisOrganization': 'none in this workspace',
      'identity:tenants.title': 'Choose a workspace',
      'identity:tenants.empty': 'You do not have access to any workspace yet.',
    })) expect(i18n.t(key, { lng: 'en' })).toBe(text);
  });

  it.each([
    ['platform.identities.read', PlatformIdentitiesPage, 'Ver identidades'],
    ['platform.retention.read', PlatformRetentionPage, 'Ver política de conservación'],
  ])('names the missing %s permission before its visible code', async (code, Component, label) => {
    show(Component, { ...platformContext(), permissions: [] });
    const primary = await screen.findByText(label, { exact: true });
    const secondary = screen.getByText(code, { exact: true });
    expect(primary).toBeVisible();
    expect(secondary).toBeVisible();
    expect(primary.nextElementSibling).toBe(secondary);
    expect(primary).toHaveClass('MuiTypography-body2');
    expect(secondary).toHaveClass('MuiTypography-caption');
  });

  it('uses infinitives for Platform load-more actions', () => {
    expect(i18n.t('platform:organizations.more')).toBe('Mostrar más organizaciones');
    expect(i18n.t('platform:identities.more')).toBe('Mostrar más cuentas');
  });

  it('shows all 29 readable permission names before their exact visible codes in the access page', async () => {
    show(IdentityContextPage, signedInContext({ permissions: Object.keys(enumsEn.permissions) }));
    await screen.findByText('Acme');
    expect(Object.keys(enumsEs.permissions)).toHaveLength(29);
    for (const [code, label] of Object.entries(enumsEs.permissions)) {
      const primary = screen.getByText(label, { exact: true });
      const secondary = screen.getByText(code, { exact: true });
      expect(primary).toBeVisible();
      expect(secondary).toBeVisible();
      expect(primary.nextElementSibling).toBe(secondary);
      expect(primary).toHaveClass('MuiTypography-body2');
      expect(secondary).toHaveClass('MuiTypography-caption');
    }
    expect(screen.getByText('Ana')).toBeVisible();
  });

  it('localizes the role table and checkbox labels while keeping exact permission codes accessible', async () => {
    server.use(get('/api/tenants/tenant-1/roles', pageOf(roles)),
      get('/api/tenants/tenant-1/permission-catalog', [{ code: 'members.read', grantable: true }]));
    show(RolesPage);
    expect(await screen.findByText('Propietario')).toBeVisible();
    expect(screen.getByText('Administrador')).toBeVisible();
    expect(screen.getByText('Owner', { exact: true })).toBeVisible();
    const checkbox = screen.getByRole('checkbox', { name: 'members.read', exact: true });
    expect(checkbox).toBeEnabled();
    expect(checkbox).toHaveAccessibleName('members.read');
    expect(checkbox).toHaveAccessibleDescription('Ver miembros');
    const descriptionId = checkbox.getAttribute('aria-describedby');
    expect(document.getElementById(descriptionId)).toHaveTextContent('Ver miembros');
    await userEvent.click(checkbox);
    expect(checkbox).toHaveAttribute('aria-describedby', descriptionId);
    expect(within(checkbox.closest('label')).getByText('Ver miembros')).toBeVisible();
    expect(within(checkbox.closest('label')).getByText('members.read')).toBeVisible();
    expect(within(screen.getByRole('table')).getAllByText('Ver miembros')).toHaveLength(3);
  });

  it('localizes roster statuses, system role chips and the role editor but preserves custom and unknown role names', async () => {
    server.use(get('/api/tenants/tenant-1/roles', pageOf(roles)),
      get('/api/tenants/tenant-1/members', pageOf([{
        membershipId: 'member-1', displayName: 'Bea', normalizedEmail: 'bea@example.test',
        status: 'Active', roleIds: ['system', 'custom', 'unknown-role-id'], isOwner: false, version: '1',
      }])));
    show(MembersPage);
    expect(await screen.findByText('Propietario')).toBeVisible();
    expect(screen.getByText('Owner', { exact: true })).toBeVisible();
    expect(screen.getByText('unknown-role-id')).toBeVisible();
    expect(screen.getByText('Activa')).toBeVisible();
    await userEvent.click(screen.getByRole('button', { name: 'Editar roles de Bea' }));
    expect(screen.getByRole('checkbox', { name: 'Propietario', exact: true })).toBeEnabled();
    expect(screen.getByRole('checkbox', { name: 'Owner', exact: true })).toBeEnabled();
    expect(screen.getByRole('checkbox', { name: 'Administrador', exact: true })).toBeEnabled();
  });

  it('formats both invitation expiry paths and updates the existing list live', async () => {
    server.use(get('/api/tenants/tenant-1/roles', pageOf(roles)),
      get('/api/tenants/tenant-1/invitations', pageOf([{
        invitationId: 'invite-1', normalizedEmail: 'invite@example.test', status: 'Pending',
        expiresAt: instant, roleIds: ['system', 'custom', 'unknown-role-id'],
      }])),
      http.post('/api/tenants/tenant-1/invitations', () => HttpResponse.json({ invitationId: 'issued-1', expiresAt: instant }, { status: 201 })));
    show(InviteMemberPage);
    expect(await screen.findByText(`vence el ${dateIn('es')}`)).toBeVisible();
    expect(screen.getByText('Pendiente')).toBeVisible();
    const table = within(screen.getByRole('table'));
    expect(table.getByText('Propietario')).toBeVisible();
    expect(table.getByText('Owner', { exact: true })).toBeVisible();
    expect(table.getByText('unknown-role-id')).toBeVisible();
    expect(screen.getByRole('checkbox', { name: 'Administrador' })).toBeEnabled();
    await userEvent.type(screen.getByLabelText('Correo electrónico'), 'next@example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar invitación' }));
    expect(await screen.findByRole('status')).toHaveTextContent(`Invitación enviada. Vence el ${dateIn('es')}.`);
    await act(() => setLanguage('en'));
    expect(screen.getByText(`expires ${dateIn('en')}`)).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent(`Invitation sent. It expires on ${dateIn('en')}.`);
  });

  it('formats the last activity of a session without translating the device name', async () => {
    server.use(get('/api/identity/sessions', [{ sessionRef: 'session-1', deviceLabel: 'Windows', isCurrent: true, createdAt: instant, expiresAt: instant, lastSeenAt: instant }]));
    show(SessionsPage);
    expect(await screen.findByText(`última actividad: ${dateIn('es')}`)).toBeVisible();
    expect(screen.getByText('Windows')).toBeVisible();
  });

  it('localizes tenant, membership and MFA states and preserves suspension option values', async () => {
    server.use(get('/api/platform/organizations', pageOf([{ tenantId: 'org-1', slug: 'acme-1', status: 'Active', suspensionReason: 'SecurityIncident' }])),
      get('/api/platform/admins', pageOf([{ membershipId: 'admin-1', normalizedEmail: 'admin@example.test', membershipStatus: 'Active', mfaStatus: 'Verified', isOwner: true }])),
      get('/api/platform/audit', pageOf([])));
    show(PlatformPanel, platformContext());
    expect(await screen.findByText('Incidente de seguridad')).toBeVisible();
    expect(screen.getByText('Activa (propietario)')).toBeVisible();
    expect(screen.getByText('Verificado')).toBeVisible();
    await userEvent.click(screen.getByRole('button', { name: 'Suspender acme-1' }));
    expect(screen.getByRole('option', { name: 'Incumplimiento de políticas' })).toHaveValue('PolicyViolation');
    await userEvent.selectOptions(screen.getByLabelText('Motivo'), 'BillingHold');
    expect(screen.getByLabelText('Motivo')).toHaveValue('BillingHold');
  });

  it('localizes account state and the separate identity suspension reason picker', async () => {
    server.use(get('/api/platform/identities', pageOf([{ identityId: 'identity-1', normalizedEmail: 'person@example.test', accountStatus: 'SelfDeactivated' }])));
    show(PlatformIdentitiesPage, platformContext());
    expect(await screen.findByText('Desactivada por su titular')).toBeVisible();
    await userEvent.click(screen.getByRole('button', { name: 'Suspender a person@example.test' }));
    expect(screen.getByRole('option', { name: 'Solicitud del operador' })).toHaveValue('OperatorRequest');
  });

  it('localizes retention policy values and receipt time while preserving operator data and durations', async () => {
    server.use(get('/api/platform/retention/policy', {
      policyId: 'policy-1', personalDataMode: 'Real', activeHoldCount: 123456,
      categories: [{ category: 'AuditEvents', retentionPeriod: 'P2Y', trigger: 'RecordCreation', action: 'Anonymise', evidenceRequired: true }],
    }), http.post('/api/platform/retention/holds', () => HttpResponse.json({
      holdId: 'hold-1', subjectIdentityId: 'identity-1', placedByMembershipId: 'admin-1', version: 1,
      reasonCode: 'Legal:2026', reference: 'CASE-2026', placedAt: instant,
    }, { status: 201 })));
    show(PlatformRetentionPage, platformContext());
    expect(await screen.findByText('Eventos de auditoría')).toBeVisible();
    for (const text of ['Reales', 'Creación del registro', 'Anonimizar', 'P2Y', '123.456']) expect(screen.getByText(text)).toBeVisible();
    await userEvent.type(screen.getByLabelText('Identidad de la persona'), 'identity-1');
    await userEvent.type(screen.getByLabelText('Código de motivo'), 'Legal:2026');
    await userEvent.type(screen.getByLabelText('Referencia'), 'CASE-2026');
    await userEvent.click(screen.getByRole('button', { name: 'Establecer bloqueo' }));
    const receipt = await screen.findByRole('status');
    for (const text of ['hold-1', 'Legal:2026', 'CASE-2026', dateIn('es')]) expect(receipt).toHaveTextContent(text);
  });

  it('resolves every system enum in both languages while preserving invariant codes as keys', () => {
    expect(i18n.t('enums:retentionCategory.OutboxSecrets', { lng: 'es' })).toBe('Secretos de mensajes salientes');
    expect(i18n.t('enums:retentionCategory.PlatformMfaMaterial', { lng: 'es' })).toBe('Datos del segundo factor de la Plataforma');
    expect(i18n.t('enums:accountStatus.SelfDeactivated', { lng: 'es' })).toBe('Desactivada por su titular');
    expect(Object.values(expectedEnglishEnumLabels).flatMap((values) => Object.keys(values))).toHaveLength(24);
    for (const [type, values] of Object.entries(enumsEn)) {
      if (type === 'permissions' || type === 'roles') continue;
      for (const [code, english] of Object.entries(values)) {
        const expectedEnglish = expectedEnglishEnumLabels[type]?.[code] ?? code;
        expect(english).toBe(expectedEnglish);
        expect(i18n.t(`enums:${type}.${code}`, { lng: 'en' })).toBe(expectedEnglish);
        expect(i18n.t(`enums:${type}.${code}`, { lng: 'es' })).toBe(enumsEs[type][code]);
        expect(enumsEs[type][code]).not.toBe(code);
      }
    }
  });
});
