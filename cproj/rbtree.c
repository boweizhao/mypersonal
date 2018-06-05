#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <inttypes.h>

#define BLACK 0
#define RED 1

typedef struct _tree_node {
    int color;
    int data;
    struct _tree_node *left;
    struct _tree_node *right;
} tree_node;

tree_node *search(tree_node *root, int data)
{
    if (!root)
    {
        return 0;
    }

    if (root->data == data)
    {
        
    }
}