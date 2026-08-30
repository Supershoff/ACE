import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { createHttpClient } from "../api/httpClient";
import { createNotificationApi, type NotificationApi } from "../api/notificationApi";
import type { CloudNotification, CloudNotificationKind } from "../api/types";
import { touchTargetStyle } from "../design-system/touchTarget";
import { useSession } from "../session/SessionContext";

const KIND_LABELS: Record<CloudNotificationKind, string> = {
  OwnershipReceived: "You received an item",
};

const LIST_ID = "notification-center-list";

export interface NotificationCenterProps {
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly notificationApi?: NotificationApi;
}

/**
 * The Notification Center (EVT-003): a compact unread badge and coalesced-event list. Visiting a
 * notification's contextual destination marks it read (CONTEXT.md: "visiting an event's destination
 * may mark its notification read automatically") -- there is no separate "mark read" control,
 * matching Progressive Interface's "no unnecessary... permanently visible advanced controls."
 */
export function NotificationCenter({ notificationApi }: NotificationCenterProps) {
  const { status, csrfToken } = useSession();
  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultApiRef = useRef<NotificationApi | null>(null);
  if (!defaultApiRef.current) {
    defaultApiRef.current = createNotificationApi(createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }));
  }
  const resolvedApi = notificationApi ?? defaultApiRef.current;

  const [isOpen, setIsOpen] = useState(false);
  const [unreadCount, setUnreadCount] = useState(0);
  const [notifications, setNotifications] = useState<readonly CloudNotification[]>([]);

  const refresh = useCallback(async () => {
    const [listResult, unreadResult] = await Promise.all([resolvedApi.list(), resolvedApi.fetchUnreadCount()]);
    if (listResult.ok && listResult.data) {
      setNotifications(listResult.data.notifications);
    }
    if (unreadResult.ok && unreadResult.data) {
      setUnreadCount(unreadResult.data.unreadCount);
    }
    // resolvedApi is stable across renders (see the defaultApiRef pattern above).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (status !== "authenticated") {
      return;
    }
    refresh();
  }, [status, refresh]);

  async function handleVisit(notification: CloudNotification) {
    setIsOpen(false);
    if (notification.isRead) {
      return;
    }
    await resolvedApi.markRead(notification.id);
    refresh();
  }

  if (status !== "authenticated") {
    return null;
  }

  return (
    <div className="notification-center">
      <button
        type="button"
        aria-expanded={isOpen}
        aria-controls={LIST_ID}
        className="notification-center__toggle"
        style={touchTargetStyle}
        onClick={() => setIsOpen((wasOpen) => !wasOpen)}
      >
        Notifications
        {unreadCount > 0 ? (
          <span className="notification-center__badge" aria-label={`${unreadCount} unread notification${unreadCount === 1 ? "" : "s"}`}>
            {unreadCount}
          </span>
        ) : null}
      </button>

      {isOpen ? (
        <ul id={LIST_ID} className="notification-center__list">
          {notifications.length === 0 ? (
            <li className="notification-center__empty">No notifications yet.</li>
          ) : (
            notifications.map((notification) => (
              <li key={notification.id} className={notification.isRead ? undefined : "notification-center__item--unread"}>
                <Link to={notification.destination} onClick={() => handleVisit(notification)}>
                  {KIND_LABELS[notification.kind]}
                  {notification.count > 1 ? ` (${notification.count})` : ""}
                </Link>
              </li>
            ))
          )}
        </ul>
      ) : null}
    </div>
  );
}
