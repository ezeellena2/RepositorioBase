import type { IconName } from "../../lib/nav";

const paths: Record<IconName | "menu" | "chevron" | "check" | "user" | "shield" | "logout" | "collapse" | "close", string> = {
  home: "M3.5 9.5 10 4l6.5 5.5V16a.75.75 0 0 1-.75.75h-3.5V12h-4.5v4.75h-3.5A.75.75 0 0 1 3.5 16V9.5Z",
  channel: "M10 3.25a6.75 6.75 0 0 0-5.87 10.08L3.5 16.5l3.25-.6A6.75 6.75 0 1 0 10 3.25Z M7.25 10h.01M10 10h.01M12.75 10h.01",
  checklist: "M4 5.5h1.5M4 10h1.5M4 14.5h1.5M8.5 5.5H16M8.5 10H16M8.5 14.5H16",
  link: "M8.5 11.5a3 3 0 0 0 4.24 0l2-2a3 3 0 0 0-4.24-4.24l-.75.75M11.5 8.5a3 3 0 0 0-4.24 0l-2 2a3 3 0 0 0 4.24 4.24l.75-.75",
  grid: "M3.75 3.75h5v5h-5v-5ZM11.25 3.75h5v5h-5v-5ZM3.75 11.25h5v5h-5v-5ZM11.25 11.25h5v5h-5v-5Z",
  activity: "M3 10h3l2-5 4 10 2-5h3",
  plug: "M7.5 3.5v3.5M12.5 3.5v3.5M5.5 7h9v2a4.5 4.5 0 0 1-9 0V7ZM10 13.5v3",
  bolt: "M11 3 5 11.25h4.5L9 17l6-8.25h-4.5L11 3Z",
  users: "M7.5 10a2.75 2.75 0 1 0 0-5.5 2.75 2.75 0 0 0 0 5.5ZM2.75 16.25a4.75 4.75 0 0 1 9.5 0M13 4.75a2.75 2.75 0 0 1 0 5.25M14.5 11.75a4.75 4.75 0 0 1 2.75 4.5",
  phone: "M6.75 3.25h6.5a1 1 0 0 1 1 1v11.5a1 1 0 0 1-1 1h-6.5a1 1 0 0 1-1-1V4.25a1 1 0 0 1 1-1ZM9 14.5h2",
  menu: "M3.5 6h13M3.5 10h13M3.5 14h13",
  chevron: "m6 8 4 4 4-4",
  check: "m4.5 10.5 3.5 3.5 7.5-8",
  user: "M10 10a3.25 3.25 0 1 0 0-6.5 3.25 3.25 0 0 0 0 6.5ZM4 17a6 6 0 0 1 12 0",
  shield: "M10 3 4.5 5.25v4.5c0 3.5 2.5 6 5.5 7.25 3-1.25 5.5-3.75 5.5-7.25v-4.5L10 3ZM7.75 10l1.5 1.5 3-3",
  logout: "M8 4.5H5.25a1 1 0 0 0-1 1v9a1 1 0 0 0 1 1H8M12.5 13.5 16 10l-3.5-3.5M16 10H8",
  collapse: "m11.5 6-4 4 4 4",
  close: "m5.5 5.5 9 9M14.5 5.5l-9 9",
};

interface IconProps {
  name: keyof typeof paths;
  size?: number;
  className?: string;
}

export function Icon({ name, size = 20, className }: IconProps) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 20 20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d={paths[name]} />
    </svg>
  );
}
