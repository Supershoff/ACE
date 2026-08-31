import type { HttpClient, HttpResult } from "./httpClient";
import type { CloudNotificationListResponse, CloudNotificationUnreadCountResponse } from "./types";

export interface NotificationApi {
  list(): Promise<HttpResult<CloudNotificationListResponse>>;
  fetchUnreadCount(): Promise<HttpResult<CloudNotificationUnreadCountResponse>>;
  /** CONTEXT.md: "visiting an event's destination may mark its notification read automatically." */
  markRead(notificationId: string): Promise<HttpResult<void>>;
}

export function createNotificationApi(httpClient: HttpClient): NotificationApi {
  return {
    list: () => httpClient.get<CloudNotificationListResponse>("/notifications"),
    fetchUnreadCount: () => httpClient.get<CloudNotificationUnreadCountResponse>("/notifications/unread-count"),
    markRead: (notificationId) => httpClient.post<void>(`/notifications/${notificationId}/read`),
  };
}
