import http from "node:http";

const tokenPayload = (value) =>
  Buffer.from(JSON.stringify(value)).toString("base64url");
const validToken = [
  tokenPayload({ alg: "HS256", typ: "JWT" }),
  tokenPayload({ sub: "test-user", exp: 4102444800 }),
  "test-signature",
].join(".");

const server = http.createServer((request, response) => {
  response.setHeader("Content-Type", "application/json");

  if (request.url === "/api/auth/login" && request.method === "POST") {
    let body = "";
    request.on("data", (chunk) => {
      body += chunk;
    });
    request.on("end", () => {
      const credentials = JSON.parse(body || "{}");
      if (
        credentials.username !== "valid-user" ||
        credentials.password !== "valid-password"
      ) {
        response.writeHead(401);
        response.end(
          JSON.stringify({
            message: "Kullanıcı adı veya şifre hatalı.",
          })
        );
        return;
      }

      response.writeHead(200);
      response.end(
        JSON.stringify({
          token: validToken,
          expiresInSeconds: 43200,
          user: {
            id: "test-user",
            username: "valid-user",
            fullName: "Valid User",
            roles: ["Admin"],
          },
        })
      );
    });
    return;
  }

  if (request.url === "/api/auth/me") {
    const token = request.headers.authorization?.replace(
      /^Bearer\s+/,
      ""
    );

    if (token === validToken) {
      response.writeHead(200);
      response.end(
        JSON.stringify({
          roles: ["Admin"],
          permissions: ["system.users.manage"],
        })
      );
      return;
    }

    if (token === "limited") {
      response.writeHead(200);
      response.end(
        JSON.stringify({
          roles: ["Tekniker"],
          permissions: [],
        })
      );
      return;
    }

    response.writeHead(401);
    response.end(
      JSON.stringify({
        message: "Oturum geçersiz veya süresi dolmuş.",
      })
    );
    return;
  }

  response.writeHead(404);
  response.end(JSON.stringify({ message: "Not found" }));
});

server.listen(5155, "127.0.0.1", () => {
  console.log("Mock auth backend listening on 5155");
});
