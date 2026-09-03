namespace UpdateWatch2.Server.Auth;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Username);

public record MeResponse(bool Authenticated, string? Username);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
