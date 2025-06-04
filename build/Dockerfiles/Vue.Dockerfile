FROM node:slim

# Consider installing http-server only if truly needed for the final stage,
# or use a multi-stage build to keep the final image smaller.
# RUN npm install -g http-server

WORKDIR /app

# --- Corrected COPY instructions ---
# The source paths are relative to the build context (YourProjectRoot/)

# 1. Copy package.json and package-lock.json from their location within the build context
COPY TeeTimeTally.UI/Client/package*.json ./

# 2. Install dependencies
RUN npm install

# 3. Copy all other files from the client app's directory into the current WORKDIR
COPY TeeTimeTally.UI/Client/. ./
# --- End of Corrected COPY instructions ---

# Build app for production with minification
RUN npm run build

# Expose the port your app will run on
EXPOSE 8080

# Command to serve your built app (assuming it's in a 'dist' folder)
# If you need http-server, install it here if not done globally or use a multi-stage build
RUN npm install -g http-server
CMD [ "http-server", "dist", "-a", "::", "-p", "8080" ]
