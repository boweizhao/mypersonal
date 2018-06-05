#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <inttypes.h>

typedef struct _dlink_node {
	uintptr_t link_base;
	int data;
} dlink_node;

dlink_node *dlink_nei(dlink_node *nei, dlink_node *cur) {
	if (!cur) {
		return 0;
	}

	return (dlink_node *)(cur->link_base ^ (uintptr_t)nei);
}

dlink_node *dlink_insert(dlink_node *left, dlink_node *right, int data) {
	dlink_node *new_node = (dlink_node *)malloc(sizeof(dlink_node));
	new_node->data = data;
	new_node->link_base = (uintptr_t)left ^ (uintptr_t)right;
	if (left) {
		left->link_base = left->link_base ^ (uintptr_t)right ^ (uintptr_t)new_node;
	}

	if (right) {
		right->link_base = right->link_base ^ (uintptr_t)left ^ (uintptr_t)new_node;
	}

	return new_node;
}

void dlink_delete(dlink_node *nei, dlink_node *target) {
	if (!target) {
		return;
	}

	dlink_node *other_nei = dlink_nei(nei, target);
	if (nei) {
		nei->link_base = nei->link_base ^ (uintptr_t)target ^ (uintptr_t)other_nei;
	}

	if (other_nei) {
		other_nei->link_base = other_nei->link_base ^ (uintptr_t)target ^ (uintptr_t)nei;
	}

	free(target);
}

void dlink_travel(dlink_node *head) {
	int first = 1;
	dlink_node *prev = 0;
	while (head)
	{
		if (!first)
		{
			printf("->");
		}
		else
		{
			first = 0;
		}

		printf("%d", head->data);
		dlink_node *cur = head;
		head = dlink_nei(prev, cur);
		prev = cur;
	}

	printf("\n");
}

int main()
{
	dlink_node *head = dlink_insert(0, 0, 0);
	dlink_node *second_last = head;
	for (int idx = 1; idx < 100; idx++)
	{
		second_last = dlink_insert(second_last, 0, idx);
	}

	dlink_travel(head);

	dlink_node *next;
	while(next = dlink_nei(0, head))
	{
		dlink_delete(next, head);
		head = next;
		dlink_travel(head);
	}
}
