import type { HttpClient, HttpResult } from "./httpClient";
import type { AdminWhoAmIResponse, HealthReadyResponse, LoginResponse, VersionResponse } from "./types";

export interface AuthApi {
  login(accountName: string, password: string): Promise<HttpResult<LoginResponse>>;
  logout(): Promise<HttpResult<unknown>>;
  fetchAdminWhoAmI(): Promise<HttpResult<AdminWhoAmIResponse>>;
  fetchHealthReady(): Promise<HttpResult<HealthReadyResponse>>;
  fetchVersion(): Promise<HttpResult<VersionResponse>>;
}

export function createAuthApi(httpClient: HttpClient): AuthApi {
  return {
    login: (accountName, password) => httpClient.post<LoginResponse>("/auth/login", { accountName, password }),
    logout: () => httpClient.post("/auth/logout", undefined),
    fetchAdminWhoAmI: () => httpClient.get<AdminWhoAmIResponse>("/admin/whoami"),
    fetchHealthReady: () => httpClient.get<HealthReadyResponse>("/health/ready"),
    fetchVersion: () => httpClient.get<VersionResponse>("/version"),
  };
}
