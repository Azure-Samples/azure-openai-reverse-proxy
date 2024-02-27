#!/bin/bash

env_file=".env"

if [ ! -f "$env_file" ]; then
	echo ".env file not found"
	exit 1
fi

source .env

if test -z "$AZURE_OPENAI_API_KEY"; then
	echo "OpenAI key is missing - \$AZURE_OPENAI_API_KEY must be set"
	exit 1
fi

url="http://127.0.0.1:8080/chat/completions?api-version=2023-05-15"

while true; do
	date +"%H:%M:%S"

	response=$(curl "$url" -s \
		-H "Content-Type: application/json" \
		-H "api-key: $AZURE_OPENAI_API_KEY" \
		-d '{"messages": [{"role": "user", "content": "How to make cookies?"}]}')

	completion=$(echo "$response" | jq -r '.choices[0].message.content')

	echo "$completion\n"
done
