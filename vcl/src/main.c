#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <sys/errno.h>
#include <sys/unistd.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <poll.h>
#include <threads.h>

#define VSNL_PORT 7663

enum : char
{
	VSNL_VERSION = 0,
	VSNL_LIST    = 1
};

struct version
{
	char major;
	char minor;
	char patch;
} version = { 0, 0, 1 };


volatile int sigint = 0;

void interrupt(int)
{
	puts("interrupted!");
	sigint = 1;
}


int handlefd(
	int fd,
	const char* buffer,
	size_t size,
	const struct pollfd* fds,
	const int nfds)
{
	for (const char* const end = buffer + size; buffer < end; buffer++)
	{
		switch (buffer[0])
		{
		case VSNL_VERSION:
			if (write(fd, &version, sizeof version) < 0)
				return errno;
			break;
		case VSNL_LIST:
		{
			unsigned short count = 0;
			/* __thread */ struct sockaddr_in peers[BUFSIZ]; // function-scope variable implicitly declared '__thread'
			for (int i = 1; i < nfds; ++i)
			{
				if (!fds[i].fd || fds[i].fd == fd)
					continue;

				struct sockaddr_in sa;
				if (getpeername(fds[i].fd, (struct sockaddr*)&sa, &(socklen_t){ sizeof sa }) < 0)
					return errno;

				peers[count++] = sa;
			}

			count = htons(count);
			write(fd, &count, sizeof count);
			write(fd, peers, sizeof peers);

			break;
		}
		default:
			printf("0x%hhX: invalid opcode\n", buffer[0]);
			break;
		}
	}

	return 0;
}

int allocfd(const struct pollfd* fds, const int nfds, int i)
{
	const int begin = i;

	for (i = (i + 1) % nfds; fds[i].fd; i = (i + 1) % nfds)
	{
		printf("allocfd: %d attempted\n", i);
		if (i != begin)
			continue;
		errno = ENOMEM;
		return -1;
	}

	return i;
}

int server()
{
	const int sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (sock < 0)
	{
		perror("open failed");
		return EXIT_FAILURE;
	}

	if (setsockopt(sock, SOL_SOCKET, SO_REUSEADDR | SO_REUSEPORT, &(int){1}, sizeof(int)) < 0)
	{
		perror("setsockopt failed");
		close(sock);
		return EXIT_FAILURE;
	}

	puts("socket opened");

	struct sockaddr_in sa = { 0 };
	sa.sin_family         = AF_INET;
	sa.sin_port           = htons(VSNL_PORT);
	sa.sin_addr.s_addr    = htonl(INADDR_ANY);

	if (bind(sock, (struct sockaddr*)&sa, sizeof sa) < 0)
	{
		perror("bind failed");
		close(sock);
		return EXIT_FAILURE;
	}

	if (listen(sock, 10) < 0)
	{
		perror("listen failed");
		close(sock);
		return EXIT_FAILURE;
	}

	puts("listening on 0.0.0.0:7663");

	signal(SIGINT, interrupt);
	signal(SIGABRT, interrupt);
	signal(SIGTERM, interrupt);

	struct pollfd fds[BUFSIZ] = { 0 };
	char buffer[BUFSIZ];
	int cur = 0;

	fds[0].fd     = sock;
	fds[0].events = POLLIN;

	while (!sigint)
	{
		/************************************
		 * ITERATE SLOT                     *
		 ************************************/
		cur = (cur + 1) % (sizeof fds / sizeof *fds);
		if (!fds[cur].fd)
			continue;

		/************************************
		 * ENSURE NON-BLOCKING              *
		 ************************************/
		if (poll(&fds[cur], 1, 0) < 0)
		{
			perror("poll failed");
			continue;
		}
		if (~fds[cur].revents & POLLIN)
			continue;

		/************************************
		 * ALLOCATE SLOT (LISTENER)         *
		 ************************************/
		if (cur == 0)
		{
			/************************************
			 * ACCEPT PEER                      *
			 ************************************/
			const int peer = accept(sock, NULL, NULL);
			if (peer < 0)
			{
				perror("accept failed");
				continue;
			}

			/************************************
			 * ALLOCATE SLOT                    *
			 ************************************/
			int next = allocfd(fds, sizeof fds / sizeof *fds, cur);
			if (next < 0)
			{
				perror("allocfd failed");
				close(peer);
				continue;
			}

			/************************************
			 * COMMIT PEER                      *
			 ************************************/
			fds[next].fd     = peer;
			fds[next].events = POLLIN;

			printf("connection opened (%u)\n", next);
		}
		/************************************
		 * HANDLE PEER                      *
		 ************************************/
		else
		{
			const ssize_t rcv = read(fds[cur].fd, buffer, sizeof buffer);
			if (rcv < 0 /* ERROR OCCURRED */)
			{
				perror("read failed");
				goto PEERFAULT;
			}
			if (!rcv /* CONNECTION CLOSED */)
			{
				printf("connection closed (%u)\n", cur);
				shutdown(fds[cur].fd, SHUT_RDWR);
				goto PEERFAULT;
			}

			if (handlefd(fds[cur].fd, buffer, rcv, fds, sizeof fds / sizeof *fds) < 0)
			{
				perror("handlefd failed");
				goto PEERFAULT;
			}

			continue;

		PEERFAULT:
			close(fds[cur].fd);
			fds[cur].fd = 0;
		}
	}

	return 0;
}

int client(int argc, char* argv[])
{
	char buffer[BUFSIZ];

	if (argc < 1)
	{
		fputs("server address required\n", stderr);
		return EXIT_FAILURE;
	}

	int sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (sock < 0)
	{
		perror("open failed");
		return EXIT_FAILURE;
	}

	struct sockaddr_in sa = {0};
	sa.sin_family         = AF_INET;
	sa.sin_port           = htons(VSNL_PORT);
	sa.sin_addr.s_addr    = inet_addr(argv[0]);

	if (connect(sock, (struct sockaddr*)&sa, sizeof sa) < 0)
	{
		perror("connect failed");
		goto EXITFAILURE;
	}

	buffer[0] = VSNL_VERSION;
	if (write(sock, buffer, 1) < 0)
	{
		perror("write failed (VSNL_VERSION)");
		goto EXITFAILURE;
	}

	struct version ver;
	if (read(sock, &ver, sizeof ver) < sizeof ver)
	{
		perror("read failed (VSNL_VERSION)");
		goto EXITFAILURE;
	}

	printf(
		"connection established to VSNL v%hhu.%hhu.%hhu (%s)\n"
		"wait for p2p connection? (y/N): ",
		ver.major, ver.minor, ver.patch,
		argv[0]);

	if (fgets(buffer, sizeof buffer, stdin)[0] == 'y')
	{
	}
	else
	{
		int index;
		unsigned short count;
		struct sockaddr_in peers[BUFSIZ];
		do
		{
			buffer[0] = VSNL_LIST;
			if (write(sock, buffer, 1) < 0)
			{
				perror("write failed (VSNL_LIST)");
				goto EXITFAILURE;
			}

			if (read(sock, buffer, 2) < 0)
			{
				perror("read failed (VSNL_LIST, count)");
				return EXIT_FAILURE;
			}
			count = ntohs(*(unsigned short*)buffer);

			for (int i = 0; i < count; ++i)
			{
				if (recv(sock, &peers[i], sizeof *peers, MSG_WAITALL) < 0)
				{
					perror("read failed (VSNL_LIST, list)");
					return EXIT_FAILURE;
				}

				printf("[%d] %s:%hu\n", i, inet_ntoa(peers[i].sin_addr), ntohs(peers[i].sin_port));
			}

			printf("connect to (out-of-range value to refresh): ");
			index = atoi(fgets(buffer, sizeof buffer, stdin));
		}
		while (0 < index && index < count);


	}

	shutdown(sock, SHUT_RDWR);
	close(sock);
	return 0;

EXITFAILURE:
	close(sock);
	return EXIT_FAILURE;
}

int main(int argc, char* argv[])
{
	if (argc < 2)
	{
		fputs("mode required!", stderr);
		return EXIT_FAILURE;
	}

	if (strcmp(argv[1], "c") == 0)
		return client(argc - 2, argv + 2);
	if (strcmp(argv[1], "s") == 0)
		return server();

	fputs("unrecognized mode detected!", stderr);
	return EXIT_FAILURE;
}
