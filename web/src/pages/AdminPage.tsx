import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminApi, versionApi } from '../api/endpoints';
import type { AdminSettings, VersionInfo } from '../api/types';

/**
 * Read-only for now — /api/admin/settings has no PUT/PATCH endpoints yet
 * (see updatewatch2-server#4). The AD-connection tab from CLAUDE.md
 * section 6.1 isn't represented yet either (see updatewatch2-server#2).
 */
export function AdminPage() {
  const { t } = useTranslation();
  const [settings, setSettings] = useState<AdminSettings | null>(null);
  const [version, setVersion] = useState<VersionInfo | null>(null);

  useEffect(() => {
    adminApi.getSettings().then(setSettings).catch(() => setSettings(null));
    versionApi.get().then(setVersion).catch(() => setVersion(null));
  }, []);

  return (
    <section>
      <h1>{t('admin.title')}</h1>

      {version && (
        <dl>
          <dt>{t('admin.serverVersion')}</dt>
          <dd>{version.server}</dd>
          <dt>{t('admin.protocolVersion')}</dt>
          <dd>{version.protocol}</dd>
          <dt>{t('admin.databaseVersion')}</dt>
          <dd>{version.database}</dd>
        </dl>
      )}

      {settings && (
        <>
          <dl>
            <dt>{t('admin.logLevel')}</dt>
            <dd>{settings.logLevel}</dd>
          </dl>

          <h2>{t('admin.bruteForce')}</h2>
          <dl>
            <dt>{t('admin.bruteForceMaxAttempts')}</dt>
            <dd>{settings.bruteForceMaxAttempts}</dd>
            <dt>{t('admin.bruteForceWindowMinutes')}</dt>
            <dd>{settings.bruteForceWindowMinutes}</dd>
            <dt>{t('admin.bruteForceLockoutMinutes')}</dt>
            <dd>{settings.bruteForceLockoutMinutes}</dd>
          </dl>

          <h2>{t('admin.notifications')}</h2>
          <dl>
            <dt>{t('admin.smtpConfigured')}</dt>
            <dd>{settings.smtpConfigured ? t('agents.yes') : t('agents.no')}</dd>
            <dt>{t('admin.notificationUpdatesPerMachine')}</dt>
            <dd>{settings.notificationUpdatesPerMachineThreshold}</dd>
            <dt>{t('admin.notificationAffectedMachines')}</dt>
            <dd>{settings.notificationAffectedMachinesThreshold}</dd>
          </dl>
        </>
      )}
    </section>
  );
}
