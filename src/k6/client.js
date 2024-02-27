import http from "k6/http";
import { check } from "k6";

export const options = {
  scenarios: {
    contacts: {
      executor: "constant-vus",
      vus: 10,
      duration: "60s",
      gracefulStop: "5s",
    },
  },
};

function getRandomPrompt() {
  const prompts = [
    "Write a short story about a robot learning to love for the first time.",
    "Compose a poem inspired by the stars and the night sky.",
    "Imagine a world where animals can talk. Write a conversation between a cat and a dog.",
    "Describe the taste of adventure in 3 sentences.",
    "Create a dialogue between a time traveler and a historian discussing the consequences of altering the past.",
  ];

  return prompts[Math.floor(Math.random() * prompts.length)];
}

export default async function () {
  const url = `${__ENV.PROXY_ENDPOINT}/chat/completions?api-version=2023-05-15`;

  const requestBody = JSON.stringify({
    messages: [{ role: "user", content: getRandomPrompt() }],
  });

  const params = {
    headers: {
      "Content-Type": "application/json",
      "api-key": `${__ENV.AZURE_OPENAI_API_KEY}`,
    },
  };

  let response = await http.asyncRequest("POST", url, requestBody, params);

  check(response, { "status was 200": (r) => r.status == 200 });

  console.log(response.json());
}
