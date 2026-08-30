import type { ButtonHTMLAttributes, ReactNode } from "react";
import { touchTargetStyle } from "../touchTarget";

export type ButtonVariant = "primary" | "secondary" | "danger";

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "type"> {
  readonly children: ReactNode;
  readonly variant?: ButtonVariant;
  readonly type?: "button" | "submit" | "reset";
}

export function Button({ children, variant = "primary", type = "button", className, style, ...rest }: ButtonProps) {
  const classNames = ["button", `button--${variant}`, className].filter(Boolean).join(" ");

  return (
    <button
      type={type}
      className={classNames}
      style={{ ...touchTargetStyle, ...style }}
      {...rest}
    >
      {children}
    </button>
  );
}
