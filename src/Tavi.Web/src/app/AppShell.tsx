import { Suspense } from 'react';
import { Outlet, NavLink, useNavigate } from 'react-router-dom';
import styles from './AppShell.module.css';
import { useOnlineHostStatus } from '@/shared/hooks/useOnlineHostStatus';
import { useHotkeys } from '@/shared/hooks/useHotkeys';
import { Globe, BookOpen, Theater, PenTool, Puzzle, Settings } from 'lucide-react';

const navItems = [
  { to: '/world', label: 'World', icon: Globe, shortcut: 'Ctrl+1' },
  { to: '/scenario', label: 'Scenario', icon: BookOpen, shortcut: 'Ctrl+2' },
  { to: '/performance', label: 'Performance', icon: Theater, shortcut: 'Ctrl+3' },
  { to: '/writing', label: 'Writing', icon: PenTool, shortcut: 'Ctrl+4' },
];

export function AppShell() {
  const navigate = useNavigate();
  const online = useOnlineHostStatus();

  useHotkeys({
    'Ctrl+1': { action: () => navigate('/world') },
    'Ctrl+2': { action: () => navigate('/scenario') },
    'Ctrl+3': { action: () => navigate('/performance') },
    'Ctrl+4': { action: () => navigate('/writing') },
  });

  return (
    <div className={styles.root}>
      {!online && (
        <div className={styles.banner} role="alert">
          本地服务连接中断
        </div>
      )}
      <header className={styles.header}>
        <div className={styles.brand}>Tavi</div>
        <div className={styles.spacer} />
        <NavLink to="/settings/model" className={styles.settingsLink}>
          <Settings size={18} />
        </NavLink>
        <div
          className={`${styles.statusDot} ${online ? styles.online : styles.offline}`}
          aria-label={online ? '服务正常' : '服务离线'}
          title={online ? '服务正常' : '服务离线'}
        />
      </header>
      <div className={styles.body}>
        <nav className={styles.nav} aria-label="主导航">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                `${styles.navItem} ${isActive ? styles.active : ''}`
              }
              title={`${item.label} (${item.shortcut})`}
            >
              <item.icon size={20} />
              <span className={styles.navLabel}>{item.label}</span>
            </NavLink>
          ))}
          <div className={styles.navDivider} />
          <NavLink
            to="/settings/extensions"
            className={({ isActive }) =>
              `${styles.navItem} ${isActive ? styles.active : ''}`
            }
            title="Extensions"
          >
            <Puzzle size={20} />
            <span className={styles.navLabel}>Extensions</span>
          </NavLink>
          <NavLink
            to="/settings/model"
            className={({ isActive }) =>
              `${styles.navItem} ${isActive ? styles.active : ''}`
            }
            title="Settings"
          >
            <Settings size={20} />
            <span className={styles.navLabel}>Settings</span>
          </NavLink>
        </nav>
        <main className={styles.main}>
          <Suspense fallback={<div className={styles.fallback}>加载中…</div>}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </div>
  );
}
