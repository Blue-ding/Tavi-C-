import React from 'react';
import styles from './LoadingSkeleton.module.css';

export function LoadingSkeleton() {
  return (
    <div className={styles.root} aria-busy="true" aria-label="加载中">
      <div className={styles.shimmer} />
    </div>
  );
}

export function SkeletonBlock({ height = 16, width = '100%', style }: { height?: number; width?: string; style?: React.CSSProperties }) {
  return (
    <div
      className={styles.block}
      style={{ height, width, ...style }}
      aria-hidden="true"
    />
  );
}
