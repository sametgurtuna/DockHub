// Types for DockHub's web widget API (window.dockhub), SDK 1.0 (apiVersion 2).
// See docs/widget-sdk.md. Reference this file from a widget's scripts:
//   /// <reference path="path/to/dockhub.d.ts" />
// or copy it next to your widget and add "// @ts-check" to your JavaScript.

declare namespace DockHub {
  /** The card width a layout (variant) gets: compact 44×44, standard 170×44, wide 260×44 dock units. */
  type Size = 'compact' | 'standard' | 'wide';

  /**
   * The manifest's defaults with the user's values laid over them, by setting key: text and choice settings are
   * strings, numbers are numbers, toggles are booleans.
   */
  type Settings = Record<string, any>;

  /** Anything JSON can hold: what storage keeps. */
  type Json = string | number | boolean | null | Json[] | { [key: string]: Json };

  interface Theme {
    mode: 'light' | 'dark';
    /** The CSS variables DockHub sets on <html> (also available as var(--dh-...) in CSS). */
    vars: {
      '--dh-text': string;
      '--dh-text-secondary': string;
      '--dh-accent': string;
      '--dh-card': string;
      '--dh-font': string;
    };
  }

  interface Notification {
    title?: string;
    body?: string;
  }

  interface MenuItem {
    /** Passed to contextMenu.onSelect. */
    id: string;
    label: string;
  }

  interface HttpRequest {
    /** https, or http for a host the user entered in a permissions.networkFromSettings setting. */
    url: string;
    /** GET (the default), POST, PUT or DELETE. */
    method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | (string & {});
    /** Host, Cookie and connection headers can't be set. */
    headers?: Record<string, string>;
    /** Text, up to 64 KB. */
    body?: string;
  }

  interface HttpResponse {
    status: number;
    /** True for a 2xx status. */
    ok: boolean;
    contentType: string | null;
    /** The answer as text, up to 1 MB. */
    body: string;
  }

  interface Api {
    /** 2 since DockHub 0.9 (SDK 1.0). */
    readonly apiVersion: number;

    /** The card's current width class; onSize tells when it changes. */
    readonly size: Size;

    settings: {
      get(): Promise<Settings>;
      onChange(handler: (values: Settings) => void): void;
    };

    /** Kept per widget copy, survives restarts; 256 KB per copy. */
    storage: {
      /** What was stored under the key, or null. */
      get(key: string): Promise<any>;
      set(key: string, value: Json): Promise<void>;
    };

    /** Needs permissions.notifications in the manifest. */
    notify(notification: Notification): Promise<void>;

    /** http(s) only; opens the default browser (at most once every two seconds). */
    openUrl(url: string): Promise<void>;

    http: {
      /** Made by DockHub, not the page: no CORS, no cookies, no redirects, 15-second timeout. */
      request(request: HttpRequest): Promise<HttpResponse>;
    };

    contextMenu: {
      /** Up to 8 items, shown at the top of the widget's right-click menu. */
      set(items: MenuItem[]): Promise<void>;
      onSelect(handler: (id: string) => void): void;
    };

    onTheme(handler: (theme: Theme) => void): void;
    onSize(handler: (size: Size) => void): void;
  }
}

interface Window {
  dockhub: DockHub.Api;
}

declare const dockhub: DockHub.Api;
