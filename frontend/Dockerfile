# Nginx image'ını kullan
FROM nginx:alpine

# Nginx default config dosyasını sil
RUN rm /etc/nginx/conf.d/default.conf

# Kendi config dosyamızı kopyala
COPY default.conf /etc/nginx/conf.d/

# Uygulama HTML/JS dosyalarını Nginx root'a kopyala
COPY . /usr/share/nginx/html
