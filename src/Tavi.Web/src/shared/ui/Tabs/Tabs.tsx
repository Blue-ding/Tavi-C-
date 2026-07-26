import React from 'react';
import styles from './Tabs.module.css';

export type TabsProps = {
  tabs: { key: string; label: string }[];
  activeKey: string;
  onChange: (key: string) => void;
};

export function Tabs({ tabs, activeKey, onChange }: TabsProps) {
  return (
    <div className={styles.root} role="tablist">
      {tabs.map((t) => (
        <button
          key={t.key}
          role="tab"
          aria-selected={t.key === activeKey}
          className={`${styles.tab} ${t.key === activeKey ? styles.active : ''}`}
          onClick={() => onChange(t.key)}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}

export function TabPanel({
  children,
  active,
}: {
  children: React.ReactNode;
  active: boolean;
}) {
  if (!active) return null;
  return <div role="tabpanel" className={styles.panel}>{children}</div>;
}
