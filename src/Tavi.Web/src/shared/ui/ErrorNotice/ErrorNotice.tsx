import React from 'react';
import styles from './ErrorNotice.module.css';

export type ErrorNoticeProps = {
  title?: string;
  message: string;
  onRetry?: () => void;
  details?: React.ReactNode;
};

export function ErrorNotice({ title = '出错了', message, onRetry, details }: ErrorNoticeProps) {
  return (
    <div className={styles.root} role="alert">
      <div className={styles.header}>
        <span className={styles.icon} aria-hidden="true">⚠</span>
        <strong>{title}</strong>
      </div>
      <p className={styles.message}>{message}</p>
      {onRetry && (
        <button className={styles.retry} onClick={onRetry}>
          重试
        </button>
      )}
      {details && <div className={styles.details}>{details}</div>}
    </div>
  );
}
