import React from 'react';
import styles from './IconButton.module.css';

export type IconButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  label: string;
  size?: 'sm' | 'md';
};

export const IconButton = React.forwardRef<HTMLButtonElement, IconButtonProps>(
  ({ label, size = 'md', className = '', children, ...rest }, ref) => {
    return (
      <button
        ref={ref}
        aria-label={label}
        className={`${styles.button} ${styles[size]} ${className}`}
        title={label}
        {...rest}
      >
        {children}
      </button>
    );
  }
);
IconButton.displayName = 'IconButton';
